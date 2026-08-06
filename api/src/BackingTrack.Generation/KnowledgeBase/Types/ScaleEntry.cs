namespace BackingTrack.Generation.KnowledgeBase.Types;

public record ScaleEntry : IEmbeddable
{
    public required string Id { get; init; }
    public required int[] Intervals { get; init; }
    public required string Description { get; init; }
    public string SearchText => Description;
}