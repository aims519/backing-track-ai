using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Tests.KnowledgeBase;

public class ChordSymbolParserTests
{
    private static ChordSymbolParser CreateParser()
    {
        (IEmbeddable Entry, float[] Vector)[] items =
        [
            (new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "min", Intervals = [0, 3, 7], Description = "Minor triad" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "7", Intervals = [0, 4, 7, 10], Description = "Dominant 7th" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "maj7", Intervals = [0, 4, 7, 11], Description = "Major 7th" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "min7", Intervals = [0, 3, 7, 10], Description = "Minor 7th" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "sus2", Intervals = [0, 2, 7], Description = "Suspended 2nd" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "sus4", Intervals = [0, 5, 7], Description = "Suspended 4th" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "dim", Intervals = [0, 3, 6], Description = "Diminished triad" }, new float[] { 1f, 0f }),
            // Non-chord entry: proves the parser only pulls its known types from ChordEntry items.
            (new StyleEntry { Id = "blues", Description = "Blues style" }, new float[] { 1f, 0f })
        ];

        InMemoryVectorStore store = new(items);
        return new ChordSymbolParser(store);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateChords_EmptyOrNullChordProvided_TreatedAsInvalid(string? chord)
    {
        ChordSymbolParser parser = CreateParser();

        // ValidateChords declares its elements non-nullable, but this test deliberately exercises
        // a null chord to prove it's treated as invalid rather than throwing - null! documents that.
        (string _, string _, bool bValid, string _) = parser.ValidateChords([chord!]).Single();

        Assert.False(bValid);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("E")]
    [InlineData("F")]
    [InlineData("G")]
    public void ValidateChords_BareRootLetter_TreatedAsMajorAndValid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (string root, string quality, bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(chord, root);
        Assert.Equal("maj", quality);
    }

    [Theory]
    [InlineData("Cmaj", "C", "maj")]
    [InlineData("Cmin", "C", "min")]
    [InlineData("C7", "C", "7")]
    [InlineData("Cmaj7", "C", "maj7")]
    [InlineData("Cmin7", "C", "min7")]
    [InlineData("Csus2", "C", "sus2")]
    [InlineData("Csus4", "C", "sus4")]
    [InlineData("Cdim", "C", "dim")]
    public void ValidateChords_KnownRootAndType_IsValid(string chord, string expectedRoot, string expectedQuality)
    {
        ChordSymbolParser parser = CreateParser();

        (string root, string quality, bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(expectedRoot, root);
        Assert.Equal(expectedQuality, quality);
    }

    [Theory]
    [InlineData("CMaj")]
    [InlineData("CmIN")]
    [InlineData("C7")]
    [InlineData("CMAJ7")]
    [InlineData("CMIN7")]
    [InlineData("CSus2")]
    [InlineData("CSUS4")]
    [InlineData("CdiM")]
    public void ValidateChords_KnownRootAndTypeVaryingCase_IsValid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (string _, string quality, bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(quality, quality.ToLower());
    }

    [Theory]
    [InlineData(" Cmaj")]
    [InlineData("Cmin ")]
    [InlineData("  C7")]
    [InlineData("Cmaj7  ")]
    [InlineData("  Cmin7  ")]
    [InlineData("   Csus2   ")]
    public void ValidateChords_KnownRootAndTypeWithLeadingOrTrailingSpaces_IsValid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (string _, string _, bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData("H7")]
    [InlineData("17")]
    [InlineData("Zmaj")]
    public void ValidateChords_UnknownRootNote_IsInvalidWithReason(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (string _, string _, bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.False(bValid);
        Assert.Contains("invalid", reason);
    }

    [Theory]
    [InlineData("Cxyz")]
    [InlineData("Cmajor")]
    [InlineData("Cm")]
    public void ValidateChords_UnknownChordType_IsInvalidWithReason(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (string _, string _, bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.False(bValid);
        Assert.Contains("invalid", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateChords_EmptyOrWhitespace_IsInvalid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (string _, string _, bool bValid, string _) = parser.ValidateChords([chord]).Single();

        Assert.False(bValid);
    }

    [Fact]
    public void ValidateChords_MultipleChords_ReturnsOneResultPerChordInOrder()
    {
        ChordSymbolParser parser = CreateParser();

        List<(string root, string quality, bool bValid, string reason)> results =
            parser.ValidateChords(["C", "Dmin7", "H7"]).ToList();

        Assert.Equal(3, results.Count);
        Assert.True(results[0].bValid);
        Assert.True(results[1].bValid);
        Assert.False(results[2].bValid);
    }

    [Fact]
    public void ValidateChords_LowercaseRootNote_IsValidAndRootIsUppercased()
    {
        ChordSymbolParser parser = CreateParser();

        (string root, string quality, bool bValid, string _) = parser.ValidateChords(["c7"]).Single();

        Assert.True(bValid);
        Assert.Equal("C", root);
        Assert.Equal("7", quality);
    }

    [Theory]
    [InlineData("C#", "C#", "maj")]
    [InlineData("F#dim", "F#", "dim")]
    [InlineData("D#min", "D#", "min")]
    public void ValidateChords_SharpRootNote_IsValid(string chord, string expectedRoot, string expectedQuality)
    {
        ChordSymbolParser parser = CreateParser();

        (string root, string quality, bool bValid, string _) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(expectedRoot, root);
        Assert.Equal(expectedQuality, quality);
    }

    [Theory]
    [InlineData("Db", "Db", "maj")]
    [InlineData("Bbdim", "Bb", "dim")]
    [InlineData("Ebmin", "Eb", "min")]
    public void ValidateChords_FlatRootNote_IsValid(string chord, string expectedRoot, string expectedQuality)
    {
        ChordSymbolParser parser = CreateParser();

        (string root, string quality, bool bValid, string _) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(expectedRoot, root);
        Assert.Equal(expectedQuality, quality);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("E")]
    [InlineData("F")]
    [InlineData("G")]
    public void ValidateRoot_NaturalRoot_IsValid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string reason) = parser.ValidateRoot(root);

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData("c", "C")]
    [InlineData("g", "G")]
    public void ValidateRoot_LowercaseRoot_IsValidAndNormalisedToUppercase(string root, string expectedRoot)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? normalisedRoot, string _) = parser.ValidateRoot(root);

        Assert.True(bValid);
        Assert.Equal(expectedRoot, normalisedRoot);
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("F#")]
    [InlineData("D#")]
    [InlineData("d#")]
    public void ValidateRoot_SharpRoot_IsValid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string _) = parser.ValidateRoot(root);

        Assert.True(bValid);
    }

    [Theory]
    [InlineData("Db")]
    [InlineData("Bb")]
    [InlineData("Eb")]
    [InlineData("eb")]
    public void ValidateRoot_FlatRoot_IsValid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string _) = parser.ValidateRoot(root);

        Assert.True(bValid);
    }

    [Theory]
    [InlineData(" C")]
    [InlineData("C ")]
    [InlineData("  F#  ")]
    public void ValidateRoot_LeadingOrTrailingWhitespace_IsValid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string _) = parser.ValidateRoot(root);

        Assert.True(bValid);
    }

    [Theory]
    [InlineData("H")]
    [InlineData("X")]
    [InlineData("1")]
    public void ValidateRoot_UnknownNoteLetter_IsInvalid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string reason) = parser.ValidateRoot(root);

        Assert.False(bValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRoot_EmptyOrWhitespace_IsInvalid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string _) = parser.ValidateRoot(root);

        Assert.False(bValid);
    }

    // A key must be JUST a root, not a full chord symbol - "Cmaj7" as a Key should not
    // silently be accepted as key "C" with the quality ignored.
    [Theory]
    [InlineData("Cmaj7")]
    [InlineData("Cdim")]
    [InlineData("Csus2")]
    [InlineData("Am")]
    public void ValidateRoot_ChordSymbolWithQualitySuffix_IsInvalid(string root)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string? _, string _) = parser.ValidateRoot(root);

        Assert.False(bValid);
    }
}