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

    private static readonly ScaleEntry MajorScale = new() { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" };
    private static readonly ScaleEntry MinorScale = new() { Id = "minor", Intervals = [0, 2, 3, 5, 7, 8, 10], Description = "Natural minor scale" };
    private static readonly ScaleEntry MixolydianScale = new() { Id = "mixolydian", Intervals = [0, 2, 4, 5, 7, 9, 10], Description = "Mixolydian mode" };

    private static readonly IReadOnlyList<ScaleEntry> AllScales = [MajorScale, MinorScale, MixolydianScale];

    private static readonly ChordEntry MajChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
    private static readonly ChordEntry MinChordEntry = new() { Id = "min", Intervals = [0, 3, 7], Description = "Minor triad" };

    [Fact]
    public void FindMatchingScales_CMajorChordInKeyOfG_ReturnsMajorAndMixolydian()
    {
        ScaleMatcher scaleMatcher = new();

        IReadOnlyList<ScaleEntry> results = scaleMatcher.FindMatchingScales(
            "G", [new MatchedChord(MajChordEntry, "C")], AllScales);

        List<string> ids = results.Select(s => s.Id).ToList();
        Assert.Contains("major", ids);
        Assert.Contains("mixolydian", ids);
        Assert.DoesNotContain("minor", ids);
    }

    [Fact]
    public void FindMatchingScales_ChordRootNotTonic_TonesAreTransposedByChordRoot()
    {
        // F# major (pitch class 6) has tones {1, 6, 10} - none of which are diatonic to
        // C major {0,2,4,5,7,9,11}. If chord tones aren't transposed by the chord's own
        // root, this incorrectly evaluates as if the chord were rooted at C (tones {0,4,7}),
        // which *is* a subset of C major, so C major would wrongly appear as a match.
        ScaleMatcher scaleMatcher = new();

        IReadOnlyList<ScaleEntry> results = scaleMatcher.FindMatchingScales(
            "C", [new MatchedChord(MajChordEntry, "F#")], AllScales);

        Assert.DoesNotContain("major", results.Select(s => s.Id));
    }

    [Fact]
    public void FindMatchingScales_MultipleChords_OnlyReturnsScalesFittingAllChords()
    {
        ScaleMatcher scaleMatcher = new();

        // C maj and G maj are both diatonic to C major, but G maj (tones {2,7,11}) is not
        // diatonic to C mixolydian (which flattens the 7th, so it lacks pitch class 11).
        IReadOnlyList<ScaleEntry> results = scaleMatcher.FindMatchingScales(
            "C", [new MatchedChord(MajChordEntry, "C"), new MatchedChord(MajChordEntry, "G")], AllScales);

        List<string> ids = results.Select(s => s.Id).ToList();
        Assert.Contains("major", ids);
        Assert.DoesNotContain("mixolydian", ids);
        Assert.DoesNotContain("minor", ids);
    }

    [Fact]
    public void FindMatchingScales_NoScaleFitsEveryChord_ReturnsEmpty()
    {
        ScaleMatcher scaleMatcher = new();

        // C maj (needs pitch class 4, the major third) and C min (needs pitch class 3, the
        // minor third) can never both be diatonic to the same scale at the same root.
        IReadOnlyList<ScaleEntry> results = scaleMatcher.FindMatchingScales(
            "C", [new MatchedChord(MajChordEntry, "C"), new MatchedChord(MinChordEntry, "C")], AllScales);

        Assert.Empty(results);
    }

    [Fact]
    public void FindMatchingScales_UnknownChordRoot_ThrowsArgumentException()
    {
        ScaleMatcher scaleMatcher = new();

        Assert.Throws<ArgumentException>(() =>
            scaleMatcher.FindMatchingScales("C", [new MatchedChord(MajChordEntry, "H")], AllScales));
    }
}
