using System.Threading.Channels;

using BackingTrack.Api.Dtos;
using BackingTrack.Generation.Jobs;

namespace BackingTrack.Api.Endpoints;

internal static class TrackEndpoints
{
    public static RouteGroupBuilder MapTrackEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/generate", Generate);
        group.MapGet("/status/{jobId:guid}", GetStatus).DisableRateLimiting();
        return group;
    }

    private static async Task<IResult> Generate(JobStore jobStore, GenerationJobQueue jobQueue, ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(nameof(TrackEndpoints));

        GenerationJob job = new() { Id = Guid.NewGuid(), State = JobStates.Pending, StartTime = DateTimeOffset.UtcNow };
        
        jobStore.AddOrUpdate(job);
        jobQueue.Writer.TryWrite(job.Id);
        
        logger.LogInformation("Generation job {Id} created in a {State} state",  job.Id, job.State);
        
        GenerationJobDto jobDto = new() { Id = job.Id, Status = job.State.ToString(), StartTime = job.StartTime, EndTime = job.EndTime };
        return Results.Ok(jobDto);
    }

    private static async Task<IResult> GetStatus(Guid jobId, JobStore jobStore)
    {
        GenerationJob? job = jobStore.TryGet(jobId);
        if (job == null)
            return Results.NotFound();
        
        GenerationJobDto jobDto = new() { Id = job.Id, Status = job.State.ToString(), StartTime = job.StartTime, EndTime = job.EndTime };
        return Results.Ok(jobDto);
    }
}