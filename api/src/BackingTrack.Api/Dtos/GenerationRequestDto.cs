using BackingTrack.Generation.Jobs;

using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api.Dtos;

public class GenerationRequestDto
{
    public string? Mode { get; init; }
    public int Tempo { get; init; }
    public IReadOnlyList<string>? Chords { get; init; }
    public string? Key { get; init; }
    public string? Style { get; init; }
}

public static class GenerationRequestDtoExtensions
{
    public static ValidationProblemDetails? Validate(this GenerationRequestDto request)
    {
        ValidationProblemDetails validationProblemDetails = new();
        
        if (!Enum.TryParse<GenerationMode>(request.Mode, out _))
            validationProblemDetails.Errors.Add(nameof(request.Mode), [$"Invalid generation mode: {request.Mode}. Accepted values are {string.Join(',', Enum.GetNames(typeof(GenerationMode)))}"]);

        if (request.Tempo < 40 || request.Tempo > 240)
            validationProblemDetails.Errors.Add(nameof(request.Tempo), [$"Tempo must be between 40 and 240 BPM, but was {request.Tempo}"
            ]);

        if (request.Chords == null || request.Chords.Count == 0)
            validationProblemDetails.Errors.Add(nameof(request.Chords), ["At least one chord must be provided"]);

        if (string.IsNullOrWhiteSpace(request.Key))
            validationProblemDetails.Errors.Add(nameof(request.Key), ["Key must be provided"]);

        return validationProblemDetails.Errors.Any() ? validationProblemDetails : null;
    }
}

