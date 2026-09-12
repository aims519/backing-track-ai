using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Tests.Steps.Critic;

public class CriticCandidateTrackTests
{
    private const int BeatsPerBar = 4;

    [Fact]
    public void Bars_TrackLengthIsAWholeNumberOfBars_ProducesExactlyThatManyBars()
    {
        CandidateTrack track = new(16, [], [], []);

        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, BeatsPerBar).Bars;

        Assert.Equal(4, bars.Count);
    }

    [Fact]
    public void Bars_NoteOnTheFirstBeatOfABar_IsAssignedToThatBar()
    {
        DrumNoteEvent note = new(BeatPosition: 4, DurationInBeats: 1, MidiNote: 36, Velocity: 90);
        CandidateTrack track = new(16, [note], [], []);

        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, BeatsPerBar).Bars;

        Assert.Empty(bars[0].Drums);
        Assert.Equal([note], bars[1].Drums);
    }

    [Fact]
    public void Bars_NoteOnTheLastBeatOfABar_IsAssignedToThatBar_NotDropped()
    {
        // Regression test: an earlier off-by-one (barEndPosition = barStart + beatsPerBar - 1,
        // then filtering strictly-less-than that) silently dropped every note starting on the
        // final beat of a bar from the output entirely - it belonged to neither bar.
        DrumNoteEvent note = new(BeatPosition: 3, DurationInBeats: 1, MidiNote: 38, Velocity: 90);
        CandidateTrack track = new(16, [note], [], []);

        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, BeatsPerBar).Bars;

        Assert.Equal([note], bars[0].Drums);
        Assert.Empty(bars[1].Drums);
    }

    [Fact]
    public void Bars_TrackLengthIsAnExactMultipleOfBeatsPerBar_DoesNotProduceATrailingEmptyBar()
    {
        // Regression test: an earlier off-by-one (barStartPosition <= LengthInBeats) added a
        // spurious extra bar with no notes whenever the track length divided evenly by the bar
        // length, so a 16-beat/4-beats-per-bar track produced 5 bars instead of 4.
        CandidateTrack track = new(16, [], [], []);

        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, BeatsPerBar).Bars;

        Assert.Equal(4, bars.Count);
    }

    [Fact]
    public void Bars_BassAndChordVoicingNotes_AreDividedIntoBarsIndependentlyOfDrums()
    {
        BassNoteEvent bassNote = new(BeatPosition: 8, DurationInBeats: 2, MidiNote: 41, Velocity: 90);
        ChordVoicingEvent chordVoicing = new(BeatPosition: 8, DurationInBeats: 4, MidiNotes: [57, 60, 65], Velocity: 90);
        CandidateTrack track = new(16, [], [bassNote], [chordVoicing]);

        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, BeatsPerBar).Bars;

        Assert.Equal([bassNote], bars[2].Bass);
        Assert.Equal([chordVoicing], bars[2].ChordVoicings);
        Assert.Empty(bars[0].Bass);
        Assert.Empty(bars[0].ChordVoicings);
    }

    [Fact]
    public void Bars_DifferentBeatsPerBar_DividesUsingTheSuppliedBarLength()
    {
        DrumNoteEvent note = new(BeatPosition: 3, DurationInBeats: 1, MidiNote: 36, Velocity: 90);
        CandidateTrack track = new(6, [note], [], []);

        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, beatsPerBar: 3).Bars;

        Assert.Equal(2, bars.Count);
        Assert.Empty(bars[0].Drums);
        Assert.Equal([note], bars[1].Drums);
    }
}