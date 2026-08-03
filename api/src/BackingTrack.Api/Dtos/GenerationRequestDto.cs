using BackingTrack.Generation.Jobs;

namespace BackingTrack.Api.Dtos;

public class GenerationRequestDto
{
    public required string Mode { get; init; }
    public required int Tempo { get; init; }
    public required IReadOnlyList<string> Chords { get; init; }
    public required string Key { get; init; }
    public string? Style { get; init; }
}

