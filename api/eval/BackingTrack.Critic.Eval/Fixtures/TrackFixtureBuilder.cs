using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Critic.Eval.Fixtures;

/// <summary>
/// Shared building blocks for hand-authored fixture tracks — a fixed 4-bar / 4-beats-per-bar
/// shape throughout, so fixtures only need to vary the one thing they're targeting.
/// </summary>
internal static class TrackFixtureBuilder
{
    internal const double LengthInBeats = 16;
    private const int Velocity = 90;

    /// <summary>
    /// Kick on 1 and 3, snare backbeat on 2 and 4, closed hihat on every eighth note — matches the
    /// "driving eighth-note hihats, backbeat snare" rock style description word for word, so the
    /// fixture doesn't contradict its own knowledge-base entry.
    /// </summary>
    internal static IReadOnlyList<DrumNoteEvent> StraightRockDrums() =>
        Enumerable.Range(0, 4).SelectMany(bar =>
        {
            double barStart = bar * 4;
            IEnumerable<DrumNoteEvent> kickAndSnare = new[]
            {
                new DrumNoteEvent(barStart + 0, 1, MidiNote: 36, Velocity),
                new DrumNoteEvent(barStart + 1, 1, MidiNote: 38, Velocity),
                new DrumNoteEvent(barStart + 2, 1, MidiNote: 36, Velocity),
                new DrumNoteEvent(barStart + 3, 1, MidiNote: 38, Velocity),
            };
            IEnumerable<DrumNoteEvent> eighthNoteHihats = Enumerable.Range(0, 8)
                .Select(eighth => new DrumNoteEvent(barStart + eighth * 0.5, 0.5, MidiNote: 42, Velocity));

            return kickAndSnare.Concat(eighthNoteHihats);
        }).ToList();

    /// <summary>One flat, unaccented hit per bar — no groove at all, for the rhythm/feel flaw fixture.</summary>
    internal static IReadOnlyList<DrumNoteEvent> RoboticSingleHitDrums() =>
        Enumerable.Range(0, 4)
            .Select(bar => new DrumNoteEvent(bar * 4, 4, MidiNote: 36, Velocity))
            .ToList();

    /// <summary>Root on beat 1, fifth on beat 3 — a simple, idiomatic walking pattern.</summary>
    internal static IReadOnlyList<BassNoteEvent> WalkingBass(IReadOnlyList<int> rootsPerBar) =>
        rootsPerBar.SelectMany((root, bar) =>
        {
            double barStart = bar * 4;
            return new[]
            {
                new BassNoteEvent(barStart + 0, 2, MidiNote: root, Velocity),
                new BassNoteEvent(barStart + 2, 2, MidiNote: root + 7, Velocity)
            };
        }).ToList();

    /// <summary>One held whole-note root per bar — flat and un-syncopated, for the rhythm/feel flaw fixture.</summary>
    internal static IReadOnlyList<BassNoteEvent> RoboticWholeNoteBass(IReadOnlyList<int> rootsPerBar) =>
        rootsPerBar.Select((root, bar) => new BassNoteEvent(bar * 4, 4, MidiNote: root, Velocity)).ToList();

    /// <summary>One sustained voicing per bar, whichever notes the fixture supplies for that bar.</summary>
    internal static IReadOnlyList<ChordVoicingEvent> ChordVoicingsPerBar(IReadOnlyList<IReadOnlyList<int>> voicingsPerBar) =>
        voicingsPerBar.Select((notes, bar) => new ChordVoicingEvent(bar * 4, 4, notes, Velocity)).ToList();

    internal static GenerationJob Job(IReadOnlyList<string> chords, string keyRoot, string style, int tempo = 100, KeyMode keyMode = KeyMode.Ionian) => new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.InProgress,
        StartTime = DateTimeOffset.UtcNow,
        Mode = GenerationMode.Precise,
        Tempo = tempo,
        Chords = chords,
        KeyRoot = keyRoot,
        KeyMode = keyMode,
        Style = style,
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
        FeedbackItems = []
    };

    internal static MatchedStyle Style(string id, string description) => new(new StyleEntry { Id = id, Description = description });

    internal static MatchedChord Chord(string id, string root, int[] intervals, string description) =>
        new(new ChordEntry { Id = id, Intervals = intervals, Description = description }, root);

    internal static MatchedScale Scale(string id, int[] intervals, string description, MatchedChord chord) =>
        new(new ScaleEntry { Id = id, Intervals = intervals, Description = description }, chord);
}