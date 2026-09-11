namespace BackingTrack.Generation.Steps.Critic;

public record CriticFinalReview(bool Pass, double Score, IReadOnlyList<CriticCitation> Citations) { }