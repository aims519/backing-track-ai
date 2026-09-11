namespace BackingTrack.Generation.Steps.Critic;

public record CriticReview(IDictionary<CriticCriterion, CriticCriterionResult> Results){}