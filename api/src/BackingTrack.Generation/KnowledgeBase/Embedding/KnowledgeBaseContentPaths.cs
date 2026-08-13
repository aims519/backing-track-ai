using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public static class KnowledgeBaseContentPaths
{
    private const string SolutionFileName = "BackingTrack.sln";

    public static string ResolveBaseDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new DirectoryNotFoundException(
                $"Could not locate {SolutionFileName} by walking up from {AppContext.BaseDirectory}");

        return Path.Combine(directory.FullName, "src", "BackingTrack.Generation", "KnowledgeBase", "Content");
    }
    
    public static string ResolveVectorFilePath<T>() where T : IEmbeddable
    {
        string type = typeof(T).Name;
        return type switch
        {
            "ChordEntry" => "chords.vector.jsonl",
            "ScaleEntry" => "scales.vector.jsonl",
            "StyleEntry" => "styles.vector.jsonl",
            _ => throw new ArgumentException($"Vector file path not defined for type, {type}")
        };
    }
    
    public static string ResolveRawContentFilePath<T>() where T : IEmbeddable
    {
        string type = typeof(T).Name;
        return type switch
        {
            "ChordEntry" => "chords.jsonl",
            "ScaleEntry" => "scales.jsonl",
            "StyleEntry" => "styles.jsonl",
            _ => throw new ArgumentException($"Content file path not defined for type, {type}")
        };
    }
}