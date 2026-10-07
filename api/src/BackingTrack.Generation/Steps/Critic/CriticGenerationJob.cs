using System.Text.Json.Serialization;

using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Steps.Critic;

/// <summary>
/// A narrow projection of the full GenerationJob to avoid serialising unnecessary
/// data in LLM prompts. Only musically relevant properties kept
/// </summary>
public record CriticGenerationJob
{
    public int Tempo { get; set; }
    public IReadOnlyList<string> Chords { get; set; }
    public string KeyRoot { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<KeyMode>))]
    public KeyMode KeyMode { get; set; }
    public string Style { get; set; }
    public int TimeSignatureBeatCount { get; set; }
    public int TimeSignatureBeatUnit { get; set; }

    public CriticGenerationJob(GenerationJob job)
    {
        Tempo = job.Tempo;
        Chords = job.Chords;
        KeyRoot = job.KeyRoot;
        KeyMode = job.KeyMode;
        Style = job.Style;
        TimeSignatureBeatCount = job.TimeSignatureBeatCount;
        TimeSignatureBeatUnit = job.TimeSignatureBeatUnit;
    }
}