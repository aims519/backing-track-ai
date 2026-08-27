namespace BackingTrack.Generation.Steps.TrackValidation;

public record TrackValidationResult(bool IsValid, IReadOnlyList<ValidationFailure> Failures);