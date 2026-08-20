using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps;

public class RetrieveStep(InMemoryVectorStore vectorStore, IQueryEmbeddingService queryEmbeddingService, ScaleMatcher scaleMatcher, ChordSymbolParser chordSymbolParser)
    : IRetrieveStep
{
    public async Task<MatchedStyle?> RetrieveTopMatchingStyleAsync(string styleQuery, CancellationToken cancellationToken = default)
    {
        float[] queryVector = await queryEmbeddingService.EmbedQueryAsync(styleQuery, cancellationToken);

        IEnumerable<(StyleEntry Entry, float Score)> results = vectorStore.Search<StyleEntry>(queryVector, 1);

        return new(results.FirstOrDefault().Entry);
    }

    public IEnumerable<ScaleEntry> RetrieveMatchingScaleEntries(string key, IReadOnlyList<MatchedChord> chords, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScaleEntry> allScaleEntries = vectorStore.Items.Where(i => i.Entry is ScaleEntry)
            .Select(i => (ScaleEntry)i.Entry)
            .ToList();
        
        IReadOnlyList<ScaleEntry> matchingScaleEntries = scaleMatcher.FindMatchingScales(key, chords, allScaleEntries);
        
        return matchingScaleEntries;
    }

    public IReadOnlyList<MatchedChord> RetrieveMatchingChords(IReadOnlyList<string> chordStrings)
    {
        IReadOnlyList<ChordEntry> allChordEntries = vectorStore.Items.Where(i => i.Entry is ChordEntry).Select(i => (ChordEntry)i.Entry).ToList();
        IEnumerable<(string root, string quality, bool bValid, string reason)> validatedChords = chordSymbolParser.ValidateChords(chordStrings);

        IList<(string root, string quality)> validChords = validatedChords.Where(c => c.bValid)
            .Select(c => (c.root, c.quality)).ToList();

        if (validChords.Any(c => allChordEntries.FirstOrDefault(e => e.Id == c.quality) is null))
            throw new InvalidOperationException("One or more chord entries requested not found in the vector store");
            
        return validChords
            .Select(v => new MatchedChord(allChordEntries.First(c => c.Id == v.quality), v.root)).ToList();
    }
}