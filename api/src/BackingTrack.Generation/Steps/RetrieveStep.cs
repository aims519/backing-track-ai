using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps;

public class RetrieveStep(InMemoryVectorStore vectorStore, IQueryEmbeddingService queryEmbeddingService, ScaleMatcher scaleMatcher, ChordSymbolParser chordSymbolParser)
    : IRetrieveStep
{
    public async Task<StyleEntry?> RetrieveTopMatchingStyleAsync(string styleQuery, CancellationToken cancellationToken = default)
    {
        float[] queryVector = await queryEmbeddingService.EmbedQueryAsync(styleQuery, cancellationToken);

        IEnumerable<(StyleEntry Entry, float Score)> results = vectorStore.Search<StyleEntry>(queryVector, 1);

        return results.FirstOrDefault().Entry;
    }

    public IEnumerable<ScaleEntry> RetrieveMatchingScaleEntries(string key, IReadOnlyList<(string chordRoot, string chordQuality)> chords, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChordEntry> allChordEntries = vectorStore.Items.Where(i => i.Entry is ChordEntry)
            .Select(i => (ChordEntry)i.Entry)
            .ToList();

        IReadOnlyList<ScaleEntry> allScaleEntries = vectorStore.Items.Where(i => i.Entry is ScaleEntry)
            .Select(i => (ScaleEntry)i.Entry)
            .ToList();

        IReadOnlyList<(string root, string quality, int[] interval)> chordsToMatchAgainst
            = chords.Select(c =>
                (c.chordRoot, c.chordQuality, allChordEntries.First(e => e.Id == c.chordQuality).Intervals))
                .ToList();
        IReadOnlyList<ScaleEntry> matchingScaleEntries = scaleMatcher.FindMatchingScales(key, chordsToMatchAgainst, allScaleEntries);
        
        return matchingScaleEntries;
    }

    public IReadOnlyList<(string chordRoot, ChordEntry chordEntry)> RetrieveMatchingChords(IReadOnlyList<string> chordStrings)
    {
        IReadOnlyList<ChordEntry> allChordEntries = vectorStore.Items.Where(i => i.Entry is ChordEntry).Select(i => (ChordEntry)i.Entry).ToList();
        IEnumerable<(string root, string quality, bool bValid, string reason)> validatedChords = chordSymbolParser.ValidateChords(chordStrings);

        return validatedChords.Where(c => c.bValid)
            .Select(v => (v.root, allChordEntries.First(c => c.Id == v.quality))).ToList();
    }
}