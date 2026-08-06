namespace BackingTrack.Generation.KnowledgeBase.Types;

public interface IEmbeddable
{
    public string Id { get; init; }
    public string SearchText { get; }
}