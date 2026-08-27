using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Tests.KnowledgeBase;

public class ScaleMatcherTests
{
    [Theory]
    [InlineData("C", 0)]
    [InlineData("C#", 1)]
    [InlineData("Db", 1)]
    [InlineData("D", 2)]
    [InlineData("D#", 3)]
    [InlineData("Eb", 3)]
    [InlineData("E", 4)]
    [InlineData("Fb", 4)]
    [InlineData("E#", 5)]
    [InlineData("F", 5)]
    [InlineData("F#", 6)]
    [InlineData("Gb", 6)]
    [InlineData("G", 7)]
    [InlineData("G#", 8)]
    [InlineData("Ab", 8)]
    [InlineData("A", 9)]
    [InlineData("A#", 10)]
    [InlineData("Bb", 10)]
    [InlineData("B", 11)]
    [InlineData("Cb", 11)]
    [InlineData("B#", 0)]
    public void RootToPitchClass_KnownRoot_ReturnsCorrectPitchClass(string root, int expectedPitchClass)
    {
        ScaleMatcher scaleMatcher = new();

        int pitchClass = scaleMatcher.RootToPitchClass(root);

        Assert.Equal(expectedPitchClass, pitchClass);
    }

    [Theory]
    [InlineData("H")]
    [InlineData("")]
    [InlineData("X#")]
    public void RootToPitchClass_UnknownRoot_ReturnsMinusOne(string root)
    {
        ScaleMatcher scaleMatcher = new();

        int pitchClass = scaleMatcher.RootToPitchClass(root);

        Assert.Equal(-1, pitchClass);
    }

    [Fact]
    public void RootToPitchClass_EnharmonicEquivalents_ReturnSamePitchClass()
    {
        ScaleMatcher scaleMatcher = new();

        Assert.Equal(scaleMatcher.RootToPitchClass("C#"), scaleMatcher.RootToPitchClass("Db"));
        Assert.Equal(scaleMatcher.RootToPitchClass("D#"), scaleMatcher.RootToPitchClass("Eb"));
        Assert.Equal(scaleMatcher.RootToPitchClass("F#"), scaleMatcher.RootToPitchClass("Gb"));
        Assert.Equal(scaleMatcher.RootToPitchClass("A#"), scaleMatcher.RootToPitchClass("Bb"));
        Assert.Equal(scaleMatcher.RootToPitchClass("E"), scaleMatcher.RootToPitchClass("Fb"));
        Assert.Equal(scaleMatcher.RootToPitchClass("F"), scaleMatcher.RootToPitchClass("E#"));
        Assert.Equal(scaleMatcher.RootToPitchClass("B"), scaleMatcher.RootToPitchClass("Cb"));
        Assert.Equal(scaleMatcher.RootToPitchClass("C"), scaleMatcher.RootToPitchClass("B#"));
    }

    [Fact]
    public void TransposeIntervalsByPitchClass_TransposesEachIntervalAndWrapsModulo12()
    {
        ScaleMatcher scaleMatcher = new();

        int[] result = scaleMatcher.TransposeIntervalsByPitchClass(7, [0, 4, 7]);

        Assert.Equal([7, 11, 2], result);
    }

    [Fact]
    public void TransposeIntervalsByPitchClass_ZeroPitchClass_LeavesIntervalsUnchanged()
    {
        ScaleMatcher scaleMatcher = new();

        int[] result = scaleMatcher.TransposeIntervalsByPitchClass(0, [0, 2, 4, 5, 7, 9, 11]);

        Assert.Equal([0, 2, 4, 5, 7, 9, 11], result);
    }

    [Fact]
    public void IsScaleValidForChord_ChordTonesAreSubsetOfScaleTones_ReturnsTrue()
    {
        ScaleMatcher scaleMatcher = new();

        bool result = scaleMatcher.IsScaleValidForChord([0, 4, 7], [0, 2, 4, 5, 7, 9, 11]);

        Assert.True(result);
    }

    [Fact]
    public void IsScaleValidForChord_ChordToneMissingFromScale_ReturnsFalse()
    {
        ScaleMatcher scaleMatcher = new();

        // Scale is missing pitch class 4 (the major third).
        bool result = scaleMatcher.IsScaleValidForChord([0, 4, 7], [0, 2, 3, 5, 7, 8, 10]);

        Assert.False(result);
    }

    [Fact]
    public void IsScaleValidForChord_EmptyChordTones_ReturnsTrueVacuously()
    {
        ScaleMatcher scaleMatcher = new();

        bool result = scaleMatcher.IsScaleValidForChord([], [0, 2, 4, 5, 7, 9, 11]);

        Assert.True(result);
    }

    // Mirrors src/BackingTrack.Generation/KnowledgeBase/Content/scales.jsonl so expectations
    // are checkable directly against the real KB content, not an artificial fixture.
    private static readonly ScaleEntry MajorScale = new() { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" };
    private static readonly ScaleEntry MinorScale = new() { Id = "minor", Intervals = [0, 2, 3, 5, 7, 8, 10], Description = "Natural minor scale" };
    private static readonly ScaleEntry DorianScale = new() { Id = "dorian", Intervals = [0, 2, 3, 5, 7, 9, 10], Description = "Dorian mode" };
    private static readonly ScaleEntry PhrygianScale = new() { Id = "phrygian", Intervals = [0, 1, 3, 5, 7, 8, 10], Description = "Phrygian mode" };
    private static readonly ScaleEntry LydianScale = new() { Id = "lydian", Intervals = [0, 2, 4, 6, 7, 9, 11], Description = "Lydian mode" };
    private static readonly ScaleEntry MixolydianScale = new() { Id = "mixolydian", Intervals = [0, 2, 4, 5, 7, 9, 10], Description = "Mixolydian mode" };
    private static readonly ScaleEntry MinorPentatonicScale = new() { Id = "minor_pentatonic", Intervals = [0, 3, 5, 7, 10], Description = "Minor pentatonic scale" };
    private static readonly ScaleEntry BluesScale = new() { Id = "blues_scale", Intervals = [0, 3, 5, 6, 7, 10], Description = "Blues scale" };
    private static readonly ScaleEntry PhrygianDominantScale = new() { Id = "phrygian_dominant", Intervals = [0, 1, 4, 5, 7, 8, 10], Description = "Phrygian dominant" };

    private static readonly IReadOnlyList<ScaleEntry> AllScales =
    [
        MajorScale, MinorScale, DorianScale, PhrygianScale, LydianScale, MixolydianScale,
        MinorPentatonicScale, BluesScale, PhrygianDominantScale
    ];

    // Mirrors src/BackingTrack.Generation/KnowledgeBase/Content/chords.jsonl.
    private static readonly ChordEntry MajChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
    private static readonly ChordEntry Min7ChordEntry = new() { Id = "min7", Intervals = [0, 3, 7, 10], Description = "Minor 7th" };
    private static readonly ChordEntry Dom7ChordEntry = new() { Id = "7", Intervals = [0, 4, 7, 10], Description = "Dominant 7th" };
    private static readonly ChordEntry Maj7ChordEntry = new() { Id = "maj7", Intervals = [0, 4, 7, 11], Description = "Major 7th" };

    [Fact]
    public void FindMatchingScalesForChords_MinorSeventhChordInKey_ReturnsDorianAndMinorPentatonicOnly()
    {
        // Dm7 as the ii chord in the key of C. Chord-rooted containment alone would also
        // match minor, phrygian, and blues_scale (all contain Dm7's tones when rooted at D),
        // but only dorian and minor_pentatonic, rooted at D, stay entirely within C major's
        // notes - the others introduce Bb, Eb, or Ab, which aren't diatonic to C.
        ScaleMatcher scaleMatcher = new();

        IReadOnlyList<MatchedScale> results = scaleMatcher.FindMatchingScalesForChords(
            "C", [new MatchedChord(Min7ChordEntry, "D")], AllScales);

        List<string> ids = results.Select(s => s.ScaleEntry.Id).ToList();
        Assert.Contains("dorian", ids);
        Assert.Contains("minor_pentatonic", ids);
        Assert.DoesNotContain("minor", ids);
        Assert.DoesNotContain("phrygian", ids);
        Assert.DoesNotContain("blues_scale", ids);
    }

    [Fact]
    public void FindMatchingScalesForChords_DominantSeventhChordInKey_ReturnsMixolydianOnly()
    {
        // G7 as the V chord in the key of C. Chord-rooted containment alone would also match
        // phrygian_dominant (G,Ab,B,C,D,Eb,F all contain G7's tones), but phrygian_dominant's
        // Ab and Eb aren't diatonic to C major, so only mixolydian survives the key filter.
        ScaleMatcher scaleMatcher = new();

        IReadOnlyList<MatchedScale> results = scaleMatcher.FindMatchingScalesForChords(
            "C", [new MatchedChord(Dom7ChordEntry, "G")], AllScales);

        List<string> ids = results.Select(s => s.ScaleEntry.Id).ToList();
        Assert.Contains("mixolydian", ids);
        Assert.DoesNotContain("phrygian_dominant", ids);
    }

    [Fact]
    public void FindMatchingScalesForChords_MajorSeventhTonicChordInKey_ReturnsMajorOnly()
    {
        // Cmaj7 as the I chord in the key of C. Chord-rooted containment alone would also
        // match lydian (C,D,E,F#,G,A,B contains Cmaj7's tones), but lydian's F# isn't
        // diatonic to C major, so only major survives the key filter.
        ScaleMatcher scaleMatcher = new();

        IReadOnlyList<MatchedScale> results = scaleMatcher.FindMatchingScalesForChords(
            "C", [new MatchedChord(Maj7ChordEntry, "C")], AllScales);

        List<string> ids = results.Select(s => s.ScaleEntry.Id).ToList();
        Assert.Contains("major", ids);
        Assert.DoesNotContain("lydian", ids);
    }

    [Fact]
    public void FindMatchingScalesForChords_TwoFiveOneInCMajor_ReturnsOneModePerChordFromTheSameParentScale()
    {
        // Dm7 - G7 - Cmaj7 in the key of C: the textbook answer is one mode of C major per
        // chord (D dorian, G mixolydian, C major), plus D minor pentatonic as a widely-usable
        // subset of D dorian. Nothing borrowed/chromatic (phrygian, blues_scale,
        // phrygian_dominant, lydian) should survive the key filter.
        ScaleMatcher scaleMatcher = new();

        MatchedChord dMin7 = new(Min7ChordEntry, "D");
        MatchedChord gDom7 = new(Dom7ChordEntry, "G");
        MatchedChord cMaj7 = new(Maj7ChordEntry, "C");

        IReadOnlyList<MatchedScale> results = scaleMatcher.FindMatchingScalesForChords(
            "C", [dMin7, gDom7, cMaj7], AllScales);

        List<(string ScaleId, string ChordRoot)> matches = results
            .Select(m => (m.ScaleEntry.Id, m.Chord.Root))
            .ToList();

        Assert.Equal(4, matches.Count);
        Assert.Contains(("dorian", "D"), matches);
        Assert.Contains(("minor_pentatonic", "D"), matches);
        Assert.Contains(("mixolydian", "G"), matches);
        Assert.Contains(("major", "C"), matches);
    }

    [Fact]
    public void FindMatchingScalesForChords_ChordRootNotDiatonicToKey_ReturnsEmpty()
    {
        // F# major isn't a diatonic chord in the key of C at all (C major's vii is a B
        // diminished, not F# major), so no scale rooted at F# - however it fits F# major's
        // own chord tones - stays within C major's notes.
        ScaleMatcher scaleMatcher = new();

        IReadOnlyList<MatchedScale> results = scaleMatcher.FindMatchingScalesForChords(
            "C", [new MatchedChord(MajChordEntry, "F#")], AllScales);

        Assert.Empty(results);
    }

    [Fact]
    public void FindMatchingScalesForChords_UnknownChordRoot_ThrowsArgumentException()
    {
        ScaleMatcher scaleMatcher = new();

        Assert.Throws<ArgumentException>(() =>
            scaleMatcher.FindMatchingScalesForChords("C", [new MatchedChord(MajChordEntry, "H")], AllScales));
    }

    [Fact]
    public void FindMatchingScalesForChords_UnknownKeyRoot_ThrowsArgumentException()
    {
        ScaleMatcher scaleMatcher = new();

        Assert.Throws<ArgumentException>(() =>
            scaleMatcher.FindMatchingScalesForChords("H", [new MatchedChord(MajChordEntry, "C")], AllScales));
    }

    [Fact]
    public void FindMatchingScalesForChords_SameChordAndScaleMatchedTwice_DeduplicatesResult()
    {
        // A repeated chord in the progression (e.g. Dm7 - G7 - Dm7) shouldn't produce
        // duplicate MatchedScale entries for the same (scale, chord) pairing.
        ScaleMatcher scaleMatcher = new();

        MatchedChord dMin7First = new(Min7ChordEntry, "D");
        MatchedChord dMin7Second = new(Min7ChordEntry, "D");

        IReadOnlyList<MatchedScale> results = scaleMatcher.FindMatchingScalesForChords(
            "C", [dMin7First, dMin7Second], AllScales);

        Assert.Single(results, m => m.ScaleEntry.Id == "dorian" && m.Chord.Root == "D");
    }

    [Fact]
    public void GetChordToneNames_SharpRoot_SpellsWithSharps()
    {
        ScaleMatcher scaleMatcher = new();
        IReadOnlyList<string> names = scaleMatcher.GetChordToneNames("C#", MajChordEntry.Intervals);
        Assert.Equal(["C#", "F", "G#"], names);
    }

    [Fact]
    public void GetChordToneNames_FlatRoot_SpellsWithFlats()
    {
        ScaleMatcher scaleMatcher = new();
        IReadOnlyList<string> names = scaleMatcher.GetChordToneNames("Bb", MajChordEntry.Intervals);
        Assert.Equal(["Bb", "D", "F"], names);
    }

    [Fact]
    public void GetChordToneNames_FlatRoot_RootAppearsAmongItsOwnChordTones()
    {
        // Regression test: chord tones were always spelled with sharps regardless of the
        // chord's own root spelling, so e.g. Bb's own root never appeared in its tone list.
        ScaleMatcher scaleMatcher = new();
        IReadOnlyList<string> names = scaleMatcher.GetChordToneNames("Bb", MajChordEntry.Intervals);
        Assert.Contains("Bb", names);
    }

    [Fact]
    public void GetChordToneNames_NaturalRoot_SpellsWithSharps()
    {
        ScaleMatcher scaleMatcher = new();
        IReadOnlyList<string> names = scaleMatcher.GetChordToneNames("C", MajChordEntry.Intervals);
        Assert.Equal(["C", "E", "G"], names);
    }

    [Fact]
    public void GetChordToneNames_UnknownRoot_ThrowsArgumentException()
    {
        ScaleMatcher scaleMatcher = new();
        Assert.Throws<ArgumentException>(() =>
            scaleMatcher.GetChordToneNames("H", MajChordEntry.Intervals));
    }
}