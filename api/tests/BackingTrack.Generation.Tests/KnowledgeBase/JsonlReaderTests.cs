using System.Text.Json;

using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;

using KnowledgeBaseJsonContext = BackingTrack.Generation.KnowledgeBase.Embedding.KnowledgeBaseJsonContext;

namespace BackingTrack.Generation.Tests.KnowledgeBase;

public class JsonlReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void Parse_ValidJsonlChords_ReturnsDeserializedEntriesInOrder()
    {
        string path = CreateTempFile(
            """
            {"id": "maj", "intervals": [0, 4, 7], "description": "A major triad"}
            {"id": "min", "intervals": [0, 3, 7], "description": "A minor triad"}
            """);

        List<ChordEntry> result = JsonlReader.Parse(path, KnowledgeBaseJsonContext.Default.ChordEntry).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("maj", result[0].Id);
        Assert.Equal([0, 4, 7], result[0].Intervals);
        Assert.Equal("A major triad", result[0].Description);
        Assert.Equal("min", result[1].Id);
    }

    [Fact]
    public void Parse_ValidJsonlScales_ReturnsDeserializedEntriesInOrder()
    {
        string path = CreateTempFile(
            """
            {"id": "major", "intervals": [0, 2, 4, 5, 7, 9, 11], "description": "The major scale"}
            {"id": "minor", "intervals": [0, 2, 3, 5, 7, 8, 10], "description": "The natural minor scale"}
            """);

        List<ScaleEntry> result = JsonlReader.Parse(path, KnowledgeBaseJsonContext.Default.ScaleEntry).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("major", result[0].Id);
        Assert.Equal([0, 2, 4, 5, 7, 9, 11], result[0].Intervals);
        Assert.Equal("The major scale", result[0].Description);
        Assert.Equal("minor", result[1].Id);
    }

    [Fact]
    public void Parse_ValidJsonlStyles_ReturnsDeserializedEntriesInOrder()
    {
        string path = CreateTempFile(
            """
            {"id": "blues", "description": "Built on the 12-bar blues form"}
            {"id": "jazz", "description": "Harmonically built on ii-V-I progressions"}
            """);

        List<StyleEntry> result = JsonlReader.Parse(path, KnowledgeBaseJsonContext.Default.StyleEntry).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("blues", result[0].Id);
        Assert.Equal("Built on the 12-bar blues form", result[0].Description);
        Assert.Equal("jazz", result[1].Id);
    }

    [Fact]
    public void Parse_FileDoesNotExist_ThrowsFileNotFoundException()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jsonl");

        Assert.Throws<FileNotFoundException>(() =>
            JsonlReader.Parse(missingPath, KnowledgeBaseJsonContext.Default.ChordEntry).ToList());
    }

    [Fact]
    public void Parse_MalformedLine_ThrowsJsonException()
    {
        string path = CreateTempFile("""{"id": "maj", "intervals": [0, 4, 7]""");

        Assert.Throws<JsonException>(() =>
            JsonlReader.Parse(path, KnowledgeBaseJsonContext.Default.ChordEntry).ToList());
    }

    [Fact]
    public void Parse_LineMissingRequiredProperty_ThrowsJsonException()
    {
        string path = CreateTempFile("""{"id": "maj"}""");

        Assert.Throws<JsonException>(() =>
            JsonlReader.Parse(path, KnowledgeBaseJsonContext.Default.ChordEntry).ToList());
    }

    [Fact]
    public void Parse_LineIsJsonNull_ThrowsArgumentException()
    {
        string path = CreateTempFile("null");

        Assert.Throws<ArgumentException>(() =>
            JsonlReader.Parse(path, KnowledgeBaseJsonContext.Default.ChordEntry).ToList());
    }

    [Fact]
    public void Parse_DoesNotTouchFileUntilEnumerated()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jsonl");

        IEnumerable<ChordEntry> result = JsonlReader.Parse(missingPath, KnowledgeBaseJsonContext.Default.ChordEntry);

        Assert.Throws<FileNotFoundException>(() => result.ToList());
    }

    private string CreateTempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jsonl");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string file in _tempFiles)
            File.Delete(file);
    }
}