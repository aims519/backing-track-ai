namespace BackingTrack.Generation.Eval.Model;

/// <summary>
/// The generation parameters for a case. Mirrors the API's <c>GenerationRequest</c> body so a
/// golden case is exactly the payload a client would send — nothing pipeline-internal leaks in.
/// </summary>
public sealed record GenerationInput
{
    public string Mode { get; init; } = "Precise";
    public required int Tempo { get; init; }
    public required IReadOnlyList<string> Chords { get; init; }
    public required string KeyRoot { get; init; }
    public string KeyMode { get; init; } = "Ionian";
    public required string Style { get; init; }
    public int TimeSignatureBeatCount { get; init; } = 4;
    public int TimeSignatureBeatUnit { get; init; } = 4;
}