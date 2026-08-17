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

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

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

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData("Cmaj")]
    [InlineData("Cmin")]
    [InlineData("C7")]
    [InlineData("Cmaj7")]
    [InlineData("Cmin7")]
    [InlineData("Csus2")]
    [InlineData("Csus4")]
    [InlineData("Cdim")]
    public void ValidateChords_KnownRootAndType_IsValid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
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

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
        Assert.Equal(string.Empty, reason);
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

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

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

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

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

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.False(bValid);
        Assert.Contains("invalid", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateChords_EmptyOrWhitespace_IsInvalid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string reason) = parser.ValidateChords([chord]).Single();

        Assert.False(bValid);
    }

    [Fact]
    public void ValidateChords_MultipleChords_ReturnsOneResultPerChordInOrder()
    {
        ChordSymbolParser parser = CreateParser();

        List<(bool bValid, string reason)> results = parser.ValidateChords(["C", "Dmin7", "H7"]).ToList();

        Assert.Equal(3, results.Count);
        Assert.True(results[0].bValid);
        Assert.True(results[1].bValid);
        Assert.False(results[2].bValid);
    }
    
    [Fact]
    public void ValidateChords_LowercaseRootNote_IsValid()
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string _) = parser.ValidateChords(["c7"]).Single();

        Assert.True(bValid);
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("F#dim")]
    [InlineData("D#min")]
    public void ValidateChords_SharpRootNote_IsValid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string _) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
    }
    
    [Theory]
    [InlineData("Db")]
    [InlineData("Bbdim")]
    [InlineData("Ebmin")]
    public void ValidateChords_FlatRootNote_IsValid(string chord)
    {
        ChordSymbolParser parser = CreateParser();

        (bool bValid, string _) = parser.ValidateChords([chord]).Single();

        Assert.True(bValid);
    }
}
