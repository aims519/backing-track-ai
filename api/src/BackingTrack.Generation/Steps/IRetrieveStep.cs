using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps;

public interface IRetrieveStep
{
    Task<StyleEntry?> RetrieveTopMatchingStyleAsync(string styleQuery, CancellationToken cancellationToken = default);
}