namespace BackingTrack.Generation.Eval.Running;

/// <summary>The observable result of running one case through the generation pipeline.</summary>
public sealed record GenerationRunResult
{
    public required ActualOutcome Outcome { get; init; }

    /// <summary>Attempt count reported by the workflow (1 = first candidate accepted). Null when generation never got that far.</summary>
    public int? Attempts { get; init; }

    public Guid? TrackId { get; init; }

    public string? FailureReason { get; init; }

    public required TimeSpan Elapsed { get; init; }
}

public enum ActualOutcome
{
    Succeeded,
    SucceededViaFallback,
    Failed
}