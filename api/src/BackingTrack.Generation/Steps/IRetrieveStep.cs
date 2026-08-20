using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps;

public interface IRetrieveStep
{
    Task<MatchedStyle?> RetrieveTopMatchingStyleAsync(string styleQuery, CancellationToken cancellationToken = default);
    IEnumerable<ScaleEntry> RetrieveMatchingScaleEntries(string key, IReadOnlyList<MatchedChord> matchedChords, CancellationToken cancellationToken = default);
    IReadOnlyList<MatchedChord> RetrieveMatchingChords(IReadOnlyList<string> chordStrings);
}