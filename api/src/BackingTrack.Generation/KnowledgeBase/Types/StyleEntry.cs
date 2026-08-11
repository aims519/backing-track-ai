namespace BackingTrack.Generation.KnowledgeBase.Types;

public record StyleEntry : IEmbeddable
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public string SearchText => Description;
}