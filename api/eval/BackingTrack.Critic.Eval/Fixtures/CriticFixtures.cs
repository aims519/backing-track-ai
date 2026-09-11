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

    internal static IReadOnlyList<CriticFixture> All { get; } =
    [
        GoodBasicRockComping(),
        GoodVoiceLedTriads(),
        FlawedStaticVoicingIgnoresProgression(),
        FlawedChordVoicingsUseForeignTones(),
        FlawedMetronomicRhythm()
    ];

    // Root-position triads, each one voice-led from the last by keeping common tones and moving
    // the rest by step — textbook-clean, so this should pass every dimension.
    private static CriticFixture GoodBasicRockComping()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [60, 64, 67], // C:  C4 E4 G4
            [57, 60, 64], // Am: A3 C4 E4 (keeps C, E)
            [57, 60, 65], // F:  A3 C4 F4 (keeps A, C)
            [59, 62, 67]  // G:  B3 D4 G4 (keeps G from bar 1, step-wise from F voicing)
        ];

        return new CriticFixture(
            Id: "good-basic-rock-comping",
            Description: "Root-position triads, smooth common-tone voice leading, straight rock groove.",
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

    // Every bar after the first plays pitches a semitone off every chord tone of the underlying
    // chord — small, smooth steps (so voice leading itself is fine), but consistently the wrong
    // notes. Isolates ChordToneAdherence.
    private static CriticFixture FlawedChordVoicingsUseForeignTones()
    {
        IReadOnlyList<IReadOnlyList<int>> voicings =
        [
            [60, 64, 67], // C:  C4 E4 G4 - correct, establishes the bar is otherwise normal
            [61, 65, 68], // Am (A/C/E): C#4 F4 G#4 - none are chord tones
            [54, 58, 61], // F (F/A/C):  F#3 A#3 C#4 - none are chord tones
            [56, 60, 63]  // G (G/B/D):  G#3 C4 D#4 - none are chord tones
        ];

        return new CriticFixture(
            Id: "flawed-chord-tones-off-by-a-semitone",
            Description: "Voicings for bars 2-4 sit a semitone off every chord tone of the underlying chord.",
            CandidateTrack: new CandidateTrack(LengthInBeats, StraightRockDrums(), WalkingBass(BassRoots), ChordVoicingsPerBar(voicings)),
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