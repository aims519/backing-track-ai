using BackingTrack.Api.Dtos;
using BackingTrack.Generation.Jobs;

using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api.Endpoints;

internal static class TrackEndpoints
{
    public static RouteGroupBuilder MapTrackEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/generate", Generate);
        group.MapGet("/status/{jobId:guid}", GetStatus).DisableRateLimiting();
        return group;
    }

    private static async Task<IResult> Generate(GenerationRequestDto requestDto, JobStore jobStore, GenerationJobQueue jobQueue, ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(nameof(TrackEndpoints));
        
        ValidationProblemDetails? validationProblemDetails = requestDto.Validate();
        if (validationProblemDetails != null)
            return Results.BadRequest(validationProblemDetails);

        GenerationJob job = new() 
            { 
                Id = Guid.NewGuid(), 
                State = JobStates.Pending, 
                StartTime = DateTimeOffset.UtcNow,
                Mode = Enum.Parse<GenerationMode>(requestDto.Mode),
                Tempo = requestDto.Tempo,
                Chords = requestDto.Chords,
                Key = requestDto.Key,
                Style = requestDto.Style
            };
        
        jobStore.AddOrUpdate(job);
        jobQueue.Writer.TryWrite(job.Id);
        
        logger.LogInformation("Generation job {Id} created in a {State} state",  job.Id, job.State);

        GenerationJobDto jobDto = job.ToDto();
        return Results.Ok(jobDto);
    }

    private static async Task<IResult> GetStatus(Guid jobId, JobStore jobStore)
    {
        GenerationJob? job = jobStore.TryGet(jobId);
        if (job == null)
            return Results.NotFound();
        
        GenerationJobDto jobDto = job.ToDto();
        return Results.Ok(jobDto);
    }
}