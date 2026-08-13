using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.KnowledgeBase.Embedder.Tests;

public class VectorDiffHelperTests
{
    [Fact]
    public void ComputeHash_KnownInput_ReturnsExpectedSha256Hex()
    {
        // Canonical SHA-256 digest of an empty string, verified independently via `shasum -a 256`.
        string result = VectorDiffHelper.ComputeHash("");

        Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", result);
    }

    [Fact]
    public void ComputeHash_SameInput_ProducesSameHash()
    {
        string first = VectorDiffHelper.ComputeHash("A major triad");
        string second = VectorDiffHelper.ComputeHash("A major triad");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHash_DifferentInput_ProducesDifferentHash()
    {
        string first = VectorDiffHelper.ComputeHash("A major triad");
        string second = VectorDiffHelper.ComputeHash("A minor triad");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HasContentChanged_HashMatchesCurrentContent_ReturnsFalse()
    {
        ChordEntry entry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        string currentHash = VectorDiffHelper.ComputeHash(entry.SearchText);

        bool result = VectorDiffHelper.HasContentChanged(entry, currentHash);

        Assert.False(result);
    }

    [Fact]
    public void HasContentChanged_HashDoesNotMatchCurrentContent_ReturnsTrue()
    {
        ChordEntry entry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };

        bool result = VectorDiffHelper.HasContentChanged(entry, "stale-hash");

        Assert.True(result);
    }

    [Fact]
    public void PruneVectorEntries_EntryIdNoLongerInCurrentContent_IsRemoved()
    {
        Dictionary<string, VectorEntry> existing = new()
        {
            ["maj"] = new VectorEntry("maj", "hash1", [1f, 0f]),
            ["min"] = new VectorEntry("min", "hash2", [0f, 1f])
        };
        ChordEntry currentMaj = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };

        (Dictionary<string, VectorEntry> pruned, int entriesPruned) = existing.PruneVectorEntries([currentMaj]);

        Assert.Single(pruned);
        Assert.True(pruned.ContainsKey("maj"));
        Assert.False(pruned.ContainsKey("min"));
        Assert.Equal(1, entriesPruned);
    }

    [Fact]
    public void PruneVectorEntries_AllEntriesStillPresent_NoneRemoved()
    {
        Dictionary<string, VectorEntry> existing = new()
        {
            ["maj"] = new VectorEntry("maj", "hash1", [1f, 0f]),
            ["min"] = new VectorEntry("min", "hash2", [0f, 1f])
        };
        ChordEntry currentMaj = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        ChordEntry currentMin = new() { Id = "min", Intervals = [0, 3, 7], Description = "A minor triad" };

        (Dictionary<string, VectorEntry> pruned, int entriesPruned) = existing.PruneVectorEntries([currentMaj, currentMin]);

        Assert.Equal(2, pruned.Count);
        Assert.Equal(0, entriesPruned);
    }

    [Fact]
    public void PruneVectorEntries_NoCurrentEntries_RemovesAll()
    {
        Dictionary<string, VectorEntry> existing = new()
        {
            ["maj"] = new VectorEntry("maj", "hash1", [1f, 0f]),
            ["min"] = new VectorEntry("min", "hash2", [0f, 1f])
        };

        (Dictionary<string, VectorEntry> pruned, int entriesPruned) = existing.PruneVectorEntries([]);

        Assert.Empty(pruned);
        Assert.Equal(2, entriesPruned);
    }

    [Fact]
    public void PruneVectorEntries_NoExistingEntries_ReturnsEmptyWithZeroPruned()
    {
        Dictionary<string, VectorEntry> existing = new();
        ChordEntry currentMaj = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };

        (Dictionary<string, VectorEntry> pruned, int entriesPruned) = existing.PruneVectorEntries([currentMaj]);

        Assert.Empty(pruned);
        Assert.Equal(0, entriesPruned);
    }

    [Fact]
    public void GetNewOrUpdated_EntryNotInExisting_IsIncluded()
    {
        ChordEntry newChord = new() { Id = "maj7", Intervals = [0, 4, 7, 11], Description = "A major 7th chord" };
        Dictionary<string, VectorEntry> existing = new();

        List<ChordEntry> result = VectorDiffHelper.GetNewOrUpdated([newChord], existing).ToList();

        Assert.Single(result);
        Assert.Equal("maj7", result[0].Id);
    }

    [Fact]
    public void GetNewOrUpdated_EntryHashChanged_IsIncluded()
    {
        ChordEntry chord = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        Dictionary<string, VectorEntry> existing = new()
        {
            ["maj"] = new VectorEntry("maj", "stale-hash", [1f, 0f])
        };

        List<ChordEntry> result = VectorDiffHelper.GetNewOrUpdated([chord], existing).ToList();

        Assert.Single(result);
        Assert.Equal("maj", result[0].Id);
    }

    [Fact]
    public void GetNewOrUpdated_EntryHashUnchanged_IsExcluded()
    {
        ChordEntry chord = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        Dictionary<string, VectorEntry> existing = new()
        {
            ["maj"] = new VectorEntry("maj", VectorDiffHelper.ComputeHash(chord.SearchText), [1f, 0f])
        };

        List<ChordEntry> result = VectorDiffHelper.GetNewOrUpdated([chord], existing).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void GetNewOrUpdated_MixOfNewChangedAndUnchanged_ReturnsOnlyNewAndChanged()
    {
        ChordEntry unchanged = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        ChordEntry changed = new() { Id = "min", Intervals = [0, 3, 7], Description = "A minor triad" };
        ChordEntry added = new() { Id = "dim", Intervals = [0, 3, 6], Description = "A diminished triad" };
        Dictionary<string, VectorEntry> existing = new()
        {
            ["maj"] = new VectorEntry("maj", VectorDiffHelper.ComputeHash(unchanged.SearchText), [1f, 0f]),
            ["min"] = new VectorEntry("min", "stale-hash", [0f, 1f])
        };

        List<ChordEntry> result = VectorDiffHelper.GetNewOrUpdated([unchanged, changed, added], existing).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Id == "min");
        Assert.Contains(result, e => e.Id == "dim");
        Assert.DoesNotContain(result, e => e.Id == "maj");
    }
}
