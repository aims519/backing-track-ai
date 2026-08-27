using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public static class KnowledgeBaseContentPaths
{
    private const string SolutionFileName = "BackingTrack.sln";

    /// <summary>
    /// Resolves the content directory relative to the running app itself. Used at runtime
    /// (e.g. by the API) where content is shipped alongside the published binary - see the
    /// CopyToOutputDirectory/CopyToPublishDirectory items in BackingTrack.Generation.csproj.
    /// </summary>
    public static string ResolveBaseDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "KnowledgeBase", "Content");

    /// <summary>
    /// Resolves the actual source-controlled content directory by walking up from the running
    /// process to find BackingTrack.sln. Only valid from a dev checkout - used solely by the
    /// KB embedder tool, which writes updated .vector.jsonl files back into source control
    /// rather than reading a runtime copy.
    /// </summary>
    public static string ResolveSourceContentDirectory()
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