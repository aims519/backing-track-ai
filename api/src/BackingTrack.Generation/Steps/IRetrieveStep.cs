using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps;

public interface IRetrieveStep
{
    Task<StyleEntry?> RetrieveTopMatchingStyleAsync(string styleQuery, CancellationToken cancellationToken = default);
    IEnumerable<ScaleEntry> RetrieveMatchingScaleEntries(string key, IReadOnlyList<(string chordRoot, string chordQuality)> chords, CancellationToken cancellationToken = default);
    IReadOnlyList<(string chordRoot, ChordEntry chordEntry)> RetrieveMatchingChords(IReadOnlyList<string> chordStrings);
}