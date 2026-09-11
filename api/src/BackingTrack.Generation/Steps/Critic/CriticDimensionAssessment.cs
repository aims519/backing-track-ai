using System.ComponentModel;

namespace BackingTrack.Generation.Steps.Critic;

public record CriticDimensionAssessment(
    [property: Description("The Dimension this assessment covers")] CriticDimension Dimension, 
    [property: Description("Whether the assessment has passed or not")] bool Pass, 
    [property: Description("Required when Pass is false")] string? Violation, 
    [property: Description("Required when Pass is false")] string? CitedKnowledgeBaseItemId);