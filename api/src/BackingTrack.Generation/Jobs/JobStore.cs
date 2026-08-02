using System.Collections.Concurrent;

namespace BackingTrack.Generation.Jobs;

public class JobStore
{
    private readonly ConcurrentDictionary<Guid, GenerationJob> _generationJobs = new();

    public void AddOrUpdate(GenerationJob job)
    {
        _generationJobs.AddOrUpdate(job.Id, job, (_, _) => job);
    }
    
    public void Remove(Guid jobId)
    {
        _generationJobs.TryRemove(jobId, out _);
    }
    
    public GenerationJob? TryGet(Guid jobId)
    { 
        _generationJobs.TryGetValue(jobId,  out GenerationJob? foundJob);
        return foundJob;
    }

    public IEnumerable<GenerationJob> GetJobs() => _generationJobs.Values;
}