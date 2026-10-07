namespace BackingTrack.Generation.Steps.FallbackGeneration;

public enum FallbackReason
{
    GenerateError,
    ValidationExhausted,
    CriticExhausted,
    RepairError,
    CriticError
}