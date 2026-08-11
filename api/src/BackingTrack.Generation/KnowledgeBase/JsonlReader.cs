using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace BackingTrack.Generation.KnowledgeBase;

public static class JsonlReader
{
    public static IEnumerable<T> Parse<T>(string filePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("JSONL file could not be found", filePath);
        
        foreach (string line in File.ReadLines(filePath))
        {
            T? result = JsonSerializer.Deserialize(line,  jsonTypeInfo);
            if(result == null)
                throw new ArgumentException($"JSONL file at {filePath} could not be read as it has an invalid line", filePath);
            
            yield return result;
        }
    }
}