using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps.Retrieval;

public class RetrieveStep(InMemoryVectorStore vectorStore, IQueryEmbeddingService queryEmbeddingService, ScaleMatcher scaleMatcher, ChordSymbolParser chordSymbolParser)
    : IRetrieveStep
{
    public async Task<MatchedStyle?> RetrieveTopMatchingStyles(string styleQuery, CancellationToken cancellationToken = default)
    {
        float[] queryVector = await queryEmbeddingService.EmbedQueryAsync(styleQuery, cancellationToken);

        IEnumerable<(StyleEntry Entry, float Score)> results = vectorStore.Search<StyleEntry>(queryVector, 1);

        return new(results.FirstOrDefault().Entry);
    }

    public IReadOnlyList<MatchedScale> RetrieveMatchingScales(string keyRoot, IReadOnlyList<MatchedChord> chords, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScaleEntry> allScaleEntries = vectorStore.Items.Where(i => i.Entry is ScaleEntry)
            .Select(i => (ScaleEntry)i.Entry)
            .ToList();

        IReadOnlyList<MatchedScale> matchingScaleEntries = scaleMatcher.FindMatchingScalesForChords(keyRoot, chords, allScaleEntries);

        return matchingScaleEntries;
    }

    public IReadOnlyList<MatchedChord> RetrieveMatchingChords(IReadOnlyList<string> chordStrings)
    {
        IReadOnlyList<ChordEntry> allChordEntries = vectorStore.Items.Where(i => i.Entry is ChordEntry).Select(i => (ChordEntry)i.Entry).ToList();
        IEnumerable<(string root, string quality, bool isValid, string reason)> validatedChords = chordSymbolParser.ValidateChords(chordStrings);

        IList<(string root, string quality)> validChords = validatedChords.Where(c => c.isValid)
            .Select(c => (c.root, c.quality)).ToList();

        if (validChords.Any(c => allChordEntries.FirstOrDefault(e => e.Id == c.quality) is null))
            throw new InvalidOperationException("One or more chord entries requested not found in the vector store");

        return validChords
            .Select(v => new MatchedChord(allChordEntries.First(c => c.Id == v.quality), v.root)).ToList();
    }
}