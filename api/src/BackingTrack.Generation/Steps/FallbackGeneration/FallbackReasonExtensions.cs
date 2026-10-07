namespace BackingTrack.Generation.Steps.FallbackGeneration;

public static class FallbackReasonExtensions
{
    public static string ToMetricTag(this FallbackReason fallbackReason)
    {
        return fallbackReason switch
        {
            FallbackReason.GenerateError => "generate_error",
            FallbackReason.ValidationExhausted => "validation_exhausted",
            FallbackReason.CriticExhausted => "critic_exhausted",
            FallbackReason.RepairError => "repair_error",
            FallbackReason.CriticError => "critic_error",
            _ => throw new ArgumentOutOfRangeException(nameof(fallbackReason), fallbackReason, null)
        };
    }
}