using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public static class VectorEntryReader
{
    public static Dictionary<string, VectorEntry> ReadExisting(string path, ILogger logger)
    {
        if (!File.Exists(path))
            return new Dictionary<string, VectorEntry>();

        Dictionary<string, VectorEntry> entries = new();

        foreach (string line in File.ReadLines(path))
        {
            VectorEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<VectorEntry>(line, VectorStoreJsonContext.Default.VectorEntry);
            }
            catch (JsonException e)
            {
                logger.LogWarning(e, "Skipping corrupt line in {Path} (likely a previous run was interrupted mid-write)", path);
                continue;
            }

            if (entry is not null)
                entries[entry.Id] = entry;
        }

        return entries;
    }
}