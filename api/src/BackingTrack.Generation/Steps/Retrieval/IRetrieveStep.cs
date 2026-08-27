using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps.Retrieval;

public interface IRetrieveStep
{
    Task<MatchedStyle?> RetrieveTopMatchingStyles(string styleQuery, CancellationToken cancellationToken = default);
    IReadOnlyList<MatchedScale> RetrieveMatchingScales(string keyRoot, IReadOnlyList<MatchedChord> matchedChords, CancellationToken cancellationToken = default);
    IReadOnlyList<MatchedChord> RetrieveMatchingChords(IReadOnlyList<string> chordStrings);
}