using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

using static BackingTrack.Critic.Eval.Fixtures.TrackFixtureBuilder;

namespace BackingTrack.Critic.Eval.Fixtures;

/// <summary>
/// The fixture set: a couple of known-good candidates plus one deliberately-flawed candidate per
/// <see cref="CriticDimension"/>, each flaw isolated so a wrong verdict points at a specific gap
/// in the critic's judgment rather than "something's off". Extend this list as new failure modes
/// come up in real generations — see docs/adr and the "critic step" project notes for context.
/// </summary>
internal static class CriticFixtures
{
    // C - Am - F - G, one bar each. Shared knowledge-base context for every fixture below.
    private static readonly MatchedStyle RockStyle = Style("rock", "Rock: driving eighth-note hihats, backbeat snare.");
    private static readonly MatchedChord CMaj = Chord("maj", "C", [0, 4, 7], "Major triad");
    private static readonly MatchedChord AMin = Chord("min", "A", [0, 3, 7], "Minor triad");
    private static readonly MatchedChord FMaj = Chord("maj", "F", [0, 4, 7], "Major triad");
    private static readonly MatchedChord GMaj = Chord("maj", "G", [0, 4, 7], "Major triad");
    private static readonly IReadOnlyList<MatchedChord> ProgressionChords = [CMaj, AMin, FMaj, GMaj];
    private static readonly IReadOnlyList<MatchedScale> ProgressionScales =
    [
        Scale("major", [0, 2, 4, 5, 7, 9, 11], "Major scale", CMaj),
        Scale("aeolian", [0, 2, 3, 5, 7, 8, 10], "Natural minor scale", AMin),
        Scale("major", [0, 2, 4, 5, 7, 9, 11], "Major scale", FMaj),
        Scale("major", [0, 2, 4, 5, 7, 9, 11], "Major scale", GMaj)
    ];

    private static readonly IReadOnlyList<int> BassRoots = [48, 45, 41, 43]; // C3, A2, F2, G2

    // Amin7 - Dmin7 - Emin7 - Amin7, the modal progression from the production bug this KB
    // context mirrors (job afceac62-9df3-41ae-850e-f47b8cecd615). Each chord gets the mode a
    // correctly-modeled A Aeolian key implies: A=aeolian, D=dorian, E=phrygian (all modes of the
    // same parent scale as A natural minor).
    private static readonly MatchedChord AMin7 = Chord("min7", "A", [0, 3, 7, 10], "Minor 7th");
    private static readonly MatchedChord DMin7 = Chord("min7", "D", [0, 3, 7, 10], "Minor 7th");
    private static readonly MatchedChord EMin7 = Chord("min7", "E", [0, 3, 7, 10], "Minor 7th");
    private static readonly IReadOnlyList<MatchedChord> ModalProgressionChords = [AMin7, DMin7, EMin7, AMin7];
    private static readonly IReadOnlyList<MatchedScale> ModalProgressionScales =
    [
        Scale("aeolian", [0, 2, 3, 5, 7, 8, 10], "Natural minor scale", AMin7),
        Scale("dorian", [0, 2, 3, 5, 7, 9, 10], "Dorian mode", DMin7),
        Scale("phrygian", [0, 1, 3, 5, 7, 8, 10], "Phrygian mode", EMin7),
        Scale("aeolian", [0, 2, 3, 5, 7, 8, 10], "Natural minor scale", AMin7)
    ];
    private static readonly IReadOnlyList<int> ModalBassRoots = [45, 38, 40, 45]; // A2, D2, E2, A2

    internal static IReadOnlyList<CriticFixture> All { get; } =
    [
        GoodBasicRockComping(),
        GoodVoiceLedTriads(),
        GoodModalColorTonesFromMatchedScale(),
        FlawedStaticVoicingIgnoresProgression(),
        FlawedRootNeverEstablished(),
        FlawedMetronomicRhythm()
    ];

    // Triads (root position, then inversions chosen to keep common tones), each voice-led from the
    // last by holding shared tones and moving the rest by step — textbook-clean, so this should
    // pass every dimension. An earlier version dropped the whole C voicing a third into Am (G4 to
    // A3, a seventh) and moved F to G as a parallel block; both critic models cited those moves.
    private static CriticFixture GoodBasicRockComping()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [60, 64, 67], // C:  C4 E4 G4
            [60, 64, 69], // Am: C4 E4 A4 (keeps C, E; G steps up to A)
            [60, 65, 69], // F:  C4 F4 A4 (keeps C, A; E steps up to F)
            [59, 62, 67]  // G:  B3 D4 G4 (F and G share no tones; voices move -1, -3, -2, not as a block)
        ];

        return new CriticFixture(
            Id: "good-basic-rock-comping",
            Description: "Triads voice-led by holding common tones and moving the rest by step, straight rock groove.",
            CandidateTrack: new CandidateTrack(LengthInBeats, StraightRockDrums(), WalkingBass(BassRoots), ChordVoicingsPerBar(voicings)),
            GenerationJob: Job(["C", "Am", "F", "G"], keyRoot: "C", style: "rock"),
            MatchedStyle: RockStyle,
            MatchedChords: ProgressionChords,
            MatchedScales: ProgressionScales,
            ExpectedPass: true,
            ExpectedFailingDimensions: []);
    }

    // A second good case with a different (still correct) voicing choice, so a pass/fail flip
    // between this and the first good fixture would flag over-fitting to one specific shape.
    private static CriticFixture GoodVoiceLedTriads()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [64, 67, 72], // C:  E4 G4 C5
            [64, 69, 72], // Am: E4 A4 C5 (keeps E, C)
            [65, 69, 72], // F:  F4 A4 C5 (keeps A, C)
            [62, 67, 71]  // G:  D4 G4 B4 (keeps G)
        ];

        return new CriticFixture(
            Id: "good-inverted-voice-leading",
            Description: "Inverted triads chosen to minimise movement between chords, straight rock groove.",
            CandidateTrack: new CandidateTrack(LengthInBeats, StraightRockDrums(), WalkingBass(BassRoots), ChordVoicingsPerBar(voicings)),
            GenerationJob: Job(["C", "Am", "F", "G"], keyRoot: "C", style: "rock"),
            MatchedStyle: RockStyle,
            MatchedChords: ProgressionChords,
            MatchedScales: ProgressionScales,
            ExpectedPass: true,
            ExpectedFailingDimensions: []);
    }

    // Reproduces a false positive seen in manual testing after the KeyMode fix landed: each
    // voicing adds one legitimate color tone from that bar's own MatchedScale (Dorian's raised
    // 6th over Dmin7, the 9th over Amin7, the b9 over Emin7 - all drawn from the same parent-scale
    // pool the modes share) on top of a correct min7 arpeggio. Before the ChordToneAdherence
    // instructions mentioned MatchedScales, the critic cited these as tones "outside" the chord
    // even though they're allowed by the scale matched to that exact chord.
    // The voicings are voice-led under the critic's VoiceLeading rubric (hold common tones, move
    // the rest by step), because this fixture should isolate ChordToneAdherence only. Earlier
    // versions had real VoiceLeading defects that both critic models rightly cited: bar 4 reused
    // bar 1 verbatim, a block fourth above bar 3 with zero common tones, and bars 1->2 moved four
    // voices down a fifth in parallel while bar 2->3 moved four voices up a step in parallel.
    // Now A3, C4, B4 stay put into bar 2 (E and G step down to D and F), D4, F4, B4 stay put into
    // bar 3, and bar 4 holds E3, B3 and moves the other three by a step.
    private static CriticFixture GoodModalColorTonesFromMatchedScale()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [57, 60, 64, 67, 71], // Amin7 + 9th (B):         A3 C4 E4 G4 B4
            [57, 60, 62, 65, 71], // Dmin7 + Dorian 6th (B):  A3 C4 D4 F4 B4 (keeps A, C, B; E->D, G->F)
            [52, 59, 62, 65, 71], // Emin7 + Phrygian b9 (F): E3 B3 D4 F4 B4 (keeps D, F, B; no 3rd, the bass has root and fifth)
            [52, 59, 60, 67, 69]  // Amin7 + 9th (B):         E3 B3 C4 G4 A4 (keeps E, B; D->C, F->G, B->A)
        ];

        return new CriticFixture(
            Id: "good-modal-color-tones-from-matched-scale",
            Description: "Amin7/Dmin7/Emin7/Amin7 voicings each add one color tone from that bar's own MatchedScale beyond the plain min7 arpeggio.",
            CandidateTrack: new CandidateTrack(LengthInBeats, StraightRockDrums(), WalkingBass(ModalBassRoots), ChordVoicingsPerBar(voicings)),
            GenerationJob: Job(["Amin7", "Dmin7", "Emin7", "Amin7"], keyRoot: "A", style: "rock", keyMode: KeyMode.Aeolian),
            MatchedStyle: RockStyle,
            MatchedChords: ModalProgressionChords,
            MatchedScales: ModalProgressionScales,
            ExpectedPass: true,
            ExpectedFailingDimensions: []);
    }

    // The bug that originally motivated the critic step (see project notes): every note is a
    // legal chord/scale tone for *some* bar, but the same three-note cluster repeats across all
    // four bars regardless of the chord changing underneath it. Isolates VoiceLeading — chord
    // tones for bar 1 (C) are correct, and the cluster never leaves the C major/A minor shared
    // tone set, so this is about static, non-tracking movement rather than wrong pitches.
    private static CriticFixture FlawedStaticVoicingIgnoresProgression()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [60, 64, 67], // C:  C4 E4 G4 - correct
            [60, 64, 67], // Am: same cluster, doesn't move to A/C/E
            [60, 64, 67], // F:  same cluster, doesn't move to F/A/C
            [60, 64, 67]  // G:  same cluster, doesn't move to G/B/D
        ];

        return new CriticFixture(
            Id: "flawed-static-voicing-ignores-progression",
            Description: "Same three-note cluster held across all four bars while the chord progression moves underneath it.",
            CandidateTrack: new CandidateTrack(LengthInBeats, StraightRockDrums(), WalkingBass(BassRoots), ChordVoicingsPerBar(voicings)),
            GenerationJob: Job(["C", "Am", "F", "G"], keyRoot: "C", style: "rock"),
            MatchedStyle: RockStyle,
            MatchedChords: ProgressionChords,
            MatchedScales: ProgressionScales,
            ExpectedPass: false,
            ExpectedFailingDimensions: [CriticDimension.VoiceLeading]);
    }

    // Every bar's bass and voicing use only the chord's 3rd, 5th, and 9th - the root pitch class
    // never sounds anywhere in the bar, in any instrument. Pitch-legal throughout (every note is a
    // real chord/scale tone for its bar, so ValidateStep would pass this), which is the point:
    // ChordToneAdherence is no longer about pitch legality (guaranteed by validation before the
    // critic ever sees a track) but about whether the root is actually audible. Isolates
    // ChordToneAdherence on root representation specifically, replacing the old
    // flawed-chord-tones-off-by-a-semitone fixture, whose premise (wrong pitches reaching the
    // critic) can't occur post-validation in the real pipeline.
    private static CriticFixture FlawedRootNeverEstablished()
    {
        IReadOnlyList<int> fifths = [55, 52, 60, 62]; // C's 5th (G3), Am's 5th (E3), F's 5th (C4), G's 5th (D4)
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [64, 67, 74], // C:  3rd/5th/9th - E4 G4 D5, no C
            [60, 64, 71], // Am: 3rd/5th/9th - C4 E4 B4, no A
            [57, 60, 67], // F:  3rd/5th/9th - A3 C4 G4, no F
            [59, 62, 69]  // G:  3rd/5th/9th - B3 D4 A4, no G
        ];

        return new CriticFixture(
            Id: "flawed-root-never-established",
            Description: "Bass and voicing use only the 3rd/5th/9th of each chord - never the root - in every bar.",
            CandidateTrack: new CandidateTrack(LengthInBeats, StraightRockDrums(), RoboticWholeNoteBass(fifths), ChordVoicingsPerBar(voicings)),
            GenerationJob: Job(["C", "Am", "F", "G"], keyRoot: "C", style: "rock"),
            MatchedStyle: RockStyle,
            MatchedChords: ProgressionChords,
            MatchedScales: ProgressionScales,
            ExpectedPass: false,
            ExpectedFailingDimensions: [CriticDimension.ChordToneAdherence]);
    }

    // Reuses the first good fixture's correct, well-voice-led chord tones, but the bass and drums
    // collapse to one flat, unaccented hit per bar - no backbeat, no groove. Isolates RhythmAndFeel.
    private static CriticFixture FlawedMetronomicRhythm()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [60, 64, 67], // C:  C4 E4 G4
            [57, 60, 64], // Am: A3 C4 E4
            [57, 60, 65], // F:  A3 C4 F4
            [59, 62, 67]  // G:  B3 D4 G4
        ];

        return new CriticFixture(
            Id: "flawed-metronomic-no-groove",
            Description: "Correct, well-voice-led chord tones, but bass and drums are a single flat hit per bar with no groove.",
            CandidateTrack: new CandidateTrack(LengthInBeats, RoboticSingleHitDrums(), RoboticWholeNoteBass(BassRoots), ChordVoicingsPerBar(voicings)),
            GenerationJob: Job(["C", "Am", "F", "G"], keyRoot: "C", style: "rock"),
            MatchedStyle: RockStyle,
            MatchedChords: ProgressionChords,
            MatchedScales: ProgressionScales,
            ExpectedPass: false,
            ExpectedFailingDimensions: [CriticDimension.RhythmAndFeel]);
    }
}