namespace BackingTrack.KnowledgeBase.Embedder;

internal static class KnowledgeBaseContentPaths
{
    private const string SolutionFileName = "BackingTrack.sln";

    public static string Resolve()
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
}