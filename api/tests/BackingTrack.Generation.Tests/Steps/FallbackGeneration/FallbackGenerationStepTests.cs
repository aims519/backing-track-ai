using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Tests.Steps.FallbackGeneration;

public class FallbackGenerationStepTests
{
    private static readonly ChordEntry MajChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
    private static readonly ChordEntry Min7ChordEntry = new() { Id = "min7", Intervals = [0, 3, 7, 10], Description = "Minor seventh" };
    private static readonly MatchedStyle Style = new(new StyleEntry { Id = "rock", Description = "Rock" });

    private static FallbackGenerationStep CreateStep() => new(new ScaleMatcher());

    [Fact]
    public async Task GenerateFallbackCandidateTrackAsync_SetsLengthInBeatsFromBeatsPerBarTimesChordCount()
    {
        MatchedChord[] chords = [new(MajChordEntry, "C"), new(MajChordEntry, "G"), new(MajChordEntry, "F")];

        CandidateTrack track = await CreateStep().GenerateFallbackCandidateTrackAsync(
            timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4, Style, chords, TestContext.Current.CancellationToken);

        Assert.Equal(12, track.LengthInBeats);
    }

    [Fact]
    public async Task GenerateFallbackCandidateTrackAsync_Bass_OneRootNotePerBeatOffsetByBar()
    {
        MatchedChord[] chords = [new(MajChordEntry, "C"), new(MajChordEntry, "G")];

        CandidateTrack track = await CreateStep().GenerateFallbackCandidateTrackAsync(
            timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4, Style, chords, TestContext.Current.CancellationToken);

        Assert.Equal(8, track.Bass.Count);
        Assert.All(track.Bass, n => Assert.Equal(1, n.DurationInBeats));

        // Bar 1 (C, pitch class 0): beats 0-3, root note 36
        Assert.Equal([0d, 1d, 2d, 3d], track.Bass.Take(4).Select(n => n.BeatPosition).ToArray());
        Assert.All(track.Bass.Take(4), n => Assert.Equal(36, n.MidiNote));

        // Bar 2 (G, pitch class 7): beats 4-7, root note 43
        Assert.Equal([4d, 5d, 6d, 7d], track.Bass.Skip(4).Select(n => n.BeatPosition).ToArray());
        Assert.All(track.Bass.Skip(4), n => Assert.Equal(43, n.MidiNote));
    }

    [Fact]
    public async Task GenerateFallbackCandidateTrackAsync_Drums_KickOnBeatOneHiHatEveryBeatSnareOnLastBeatOfEachBar()
    {
        MatchedChord[] chords = [new(MajChordEntry, "C"), new(MajChordEntry, "G")];

        CandidateTrack track = await CreateStep().GenerateFallbackCandidateTrackAsync(
            timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4, Style, chords, TestContext.Current.CancellationToken);

        // Per bar: 1 kick + 4 hi-hats + 1 snare = 6 events, across 2 bars = 12
        Assert.Equal(12, track.Drums.Count);

        Assert.Contains(track.Drums, n => n.BeatPosition == 0 && n.MidiNote == 36); // kick, bar 1
        Assert.Contains(track.Drums, n => n.BeatPosition == 3 && n.MidiNote == 40); // snare, bar 1
        Assert.Equal(4, track.Drums.Count(n => n.MidiNote == 42 && n.BeatPosition is >= 0 and < 4)); // hi-hats, bar 1

        Assert.Contains(track.Drums, n => n.BeatPosition == 4 && n.MidiNote == 36); // kick, bar 2
        Assert.Contains(track.Drums, n => n.BeatPosition == 7 && n.MidiNote == 40); // snare, bar 2
        Assert.Equal(4, track.Drums.Count(n => n.MidiNote == 42 && n.BeatPosition is >= 4 and < 8)); // hi-hats, bar 2
    }

    [Fact]
    public async Task GenerateFallbackCandidateTrackAsync_ChordVoicings_OneSustainedVoicingPerBarBuiltFromChordTones()
    {
        MatchedChord[] chords = [new(MajChordEntry, "C"), new(Min7ChordEntry, "D")];

        CandidateTrack track = await CreateStep().GenerateFallbackCandidateTrackAsync(
            timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4, Style, chords, TestContext.Current.CancellationToken);

        Assert.Equal(2, track.ChordVoicings.Count);

        ChordVoicingEvent cMajBar = track.ChordVoicings[0];
        Assert.Equal(0, cMajBar.BeatPosition);
        Assert.Equal(4, cMajBar.DurationInBeats);
        Assert.Equal([0, 4, 7], cMajBar.MidiNotes.Select(n => n % 12).ToArray()); // C E G

        ChordVoicingEvent dMin7Bar = track.ChordVoicings[1];
        Assert.Equal(4, dMin7Bar.BeatPosition);
        Assert.Equal(4, dMin7Bar.DurationInBeats);
        Assert.Equal([2, 5, 9, 0], dMin7Bar.MidiNotes.Select(n => n % 12).ToArray()); // D F A C
    }

    [Fact]
    public async Task GenerateFallbackCandidateTrackAsync_HighRootPitchClass_KeepsAllNotesWithinValidatorMidiRanges()
    {
        // B (pitch class 11) is the worst case for the octave-clamping in PitchClassToBassMidiNote/
        // PitchClassToChordMidiNote - the highest starting point before a clamp shift is needed.
        MatchedChord[] chords = [new(MajChordEntry, "B"), new(Min7ChordEntry, "B")];

        CandidateTrack track = await CreateStep().GenerateFallbackCandidateTrackAsync(
            timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4, Style, chords, TestContext.Current.CancellationToken);

        Assert.All(track.Bass, n => Assert.InRange(n.MidiNote, Constants.BassMidiNoteMin, Constants.BassMidiNoteMax));
        Assert.All(track.ChordVoicings.SelectMany(c => c.MidiNotes), n => Assert.InRange(n, Constants.ChordVoicingNoteMin, Constants.ChordVoicingNoteMax));
        Assert.All(track.Drums, n => Assert.InRange(n.MidiNote, Constants.GeneralMidiDrumNoteMin, Constants.GeneralMidiDrumNoteMax));
    }
}