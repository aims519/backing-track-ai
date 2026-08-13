using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace BackingTrack.Generation.Benchmarks;

// Backs ADR 0014's "a full scan over a few dozen to a few hundred entries is comfortably
// sub-millisecond" claim. EntryCount spans that range; VectorDimension matches the real
// Voyage AI output size seen in scales.vector.jsonl (1024).
//
// Runs in-process (see BenchmarkConfig below) rather than BenchmarkDotNet's default of
// spawning an isolated per-benchmark build: the Pi target has no .NET SDK installed
// (ADR 0008 - AOT-only), so there's nothing on it to spawn a `dotnet build` with. This
// trades a little process isolation for the ability to run from a self-contained publish
// with no SDK present.
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class InMemoryVectorStoreBenchmarks
{
    private const int VectorDimension = 1024;

    [Params(50, 200, 500)]
    public int EntryCount { get; set; }

    private InMemoryVectorStore _store = null!;
    private float[] _queryVector = null!;

    [GlobalSetup]
    public void Setup()
    {
        Random random = new(Seed: 42);

        (IEmbeddable Entry, float[] Vector)[] items = Enumerable.Range(0, EntryCount)
            .Select(i => (
                (IEmbeddable)new ChordEntry
                {
                    Id = $"chord-{i}",
                    Intervals = [0, 4, 7],
                    Description = $"Benchmark chord entry {i}"
                },
                RandomVector(random)))
            .ToArray();

        _store = new InMemoryVectorStore(items);
        _queryVector = RandomVector(random);
    }

    [Benchmark]
    public List<(ChordEntry Entry, float Score)> Search()
    {
        return _store.Search<ChordEntry>(_queryVector, topK: 5).ToList();
    }

    private static float[] RandomVector(Random random)
    {
        float[] vector = new float[VectorDimension];
        for (int i = 0; i < VectorDimension; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
        }

        return vector;
    }

    private class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }
}
