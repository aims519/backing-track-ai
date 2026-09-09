using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Eval.Reporting;

/// <summary>
/// Writes a <see cref="RunReport"/> as indented JSON. Keep this format append-only and stable —
/// a results directory of these files is what lets one run be compared against the last.
/// </summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(string path, RunReport report, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
    }
}