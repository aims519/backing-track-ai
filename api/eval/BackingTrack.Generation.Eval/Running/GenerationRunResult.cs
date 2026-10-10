using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>The observable result of running one case through the generation pipeline.</summary>
public sealed record GenerationRunResult
{
    public required ActualOutcome Outcome { get; init; }

    /// <summary>Attempt count reported by the workflow (1 = first candidate accepted). Null when generation never got that far.</summary>
    public int? Attempts { get; init; }

    /// <summary>
    /// Critic attempt count reported by the workflow (1 = the critic accepted the first
    /// candidate; 2+ means it rejected 1+ times before accepting one; 0 = the critic never ran,
    /// e.g. the fallback path). Null when generation never got that far.
    /// </summary>
    public int? CriticAttempts { get; init; }

    public Guid? TrackId { get; init; }

    /// <summary>The generated track itself, for expectations that inspect its notes. Null when generation failed.</summary>
    public CandidateTrack? Track { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>Why the workflow gave up on the LLM path and used the rule-based generator. Null unless <see cref="ActualOutcome.SucceededViaFallback"/>.</summary>
    public FallbackReason? FallbackReason { get; init; }

    /// <summary>Tokens spent on this case, by step, model and service tier. Empty when no LLM call completed.</summary>
    public IReadOnlyList<LlmUsage> LlmUsage { get; init; } = [];

    public required TimeSpan Elapsed { get; init; }
}

public enum ActualOutcome
{
    Succeeded,
    SucceededViaFallback,
    Failed
}