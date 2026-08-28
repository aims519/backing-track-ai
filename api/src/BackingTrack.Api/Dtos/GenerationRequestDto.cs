using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;

using Microsoft.AspNetCore.Mvc;
namespace BackingTrack.Api.Dtos;

public sealed record GenerationRequestDto
{
    public string? Mode { get; init; }
    public int Tempo { get; init; }
    public IReadOnlyList<string>? Chords { get; init; }
    public string? KeyRoot { get; init; }
    public string? Style { get; init; }
    public int TimeSignatureBeatCount { get; init; }
    public int TimeSignatureBeatUnit { get; init; }
}

internal static class GenerationRequestDtoExtensions
{
    private static readonly IReadOnlySet<int> ValidBeatUnits = new HashSet<int>() { 2, 4, 8, 16, 32 };
    private const int ValidBeatCountMin = 2;
    private const int ValidBeatCountMax = 32;

    public static ValidationProblemDetails? Validate(this GenerationRequestDto request, ChordSymbolParser chordSymbolParser)
    {
        ValidationProblemDetails validationProblemDetails = new();

        if (!Enum.TryParse<GenerationMode>(request.Mode, out _))
            validationProblemDetails.Errors.Add(nameof(request.Mode), [$"Invalid generation mode: {request.Mode}. Accepted values are {string.Join(',', Enum.GetNames<GenerationMode>())}"]);

        if (request.Tempo is < 40 or > 240)
            validationProblemDetails.Errors.Add(nameof(request.Tempo), [$"Tempo must be between 40 and 240 BPM, but was {request.Tempo}"
            ]);

        if (request.Chords == null || request.Chords.Count == 0)
            validationProblemDetails.Errors.Add(nameof(request.Chords), ["At least one chord must be provided"]);

        if (request.Chords != null)
        {
            List<string> chordErrors = chordSymbolParser.ValidateChords(request.Chords)
                .Where(r => !r.isValid)
                .Select(r => r.reason)
                .ToList();
            if (chordErrors.Count > 0)
                validationProblemDetails.Errors[nameof(request.Chords)] = chordErrors.ToArray();
        }

        if (string.IsNullOrWhiteSpace(request.KeyRoot))
            validationProblemDetails.Errors.Add(nameof(request.KeyRoot), ["KeyRoot must be provided"]);
        else if (!chordSymbolParser.ValidateRoot(request.KeyRoot).isValid)
            validationProblemDetails.Errors.Add(nameof(request.KeyRoot), [$"KeyRoot '{request.KeyRoot}' is not a valid root note"]);

        if (request.TimeSignatureBeatCount < ValidBeatCountMin || request.TimeSignatureBeatCount > ValidBeatCountMax)
            validationProblemDetails.Errors.Add(nameof(request.TimeSignatureBeatCount), [$"Time signature beat count must be between {ValidBeatCountMin} and {ValidBeatCountMax}"]);

        if (!ValidBeatUnits.Contains(request.TimeSignatureBeatUnit))
            validationProblemDetails.Errors.Add(nameof(request.TimeSignatureBeatUnit), [$"Invalid beat unit {request.TimeSignatureBeatUnit} provided. Must be in [{string.Join(',', ValidBeatUnits)}]"]);

        if (string.IsNullOrWhiteSpace(request.Style))
            validationProblemDetails.Errors.Add(nameof(request.Style), ["Style guidance must be provided"]);

        return validationProblemDetails.Errors.Count > 0 ? validationProblemDetails : null;
    }
}