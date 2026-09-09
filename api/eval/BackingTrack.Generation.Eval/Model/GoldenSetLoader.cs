using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Eval.Model;

/// <summary>
/// Loads the golden set from <c>*.jsonl</c> files: one <see cref="EvalCase"/> per non-blank line.
/// Whole-line <c>//</c> comments are allowed so a case can carry a note next to it.
/// </summary>
public static class GoldenSetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<EvalCase> Load(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Golden set directory not found: {directory}");

        List<EvalCase> cases = [];

        foreach (string file in Directory.EnumerateFiles(directory, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                try
                {
                    EvalCase evalCase = JsonSerializer.Deserialize<EvalCase>(trimmed, JsonOptions)
                        ?? throw new JsonException("line deserialized to null");
                    cases.Add(evalCase);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(file)}:{lineNumber} is not a valid EvalCase — {ex.Message}", ex);
                }
            }
        }

        List<string> duplicateIds = cases
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            throw new InvalidOperationException($"Duplicate case ids in the golden set: {string.Join(", ", duplicateIds)}");

        return cases;
    }
}