using BackingTrack.Api.Dtos;
using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BackingTrack.Api.Endpoints;

internal static class TrackEndpoints
{
    public static RouteGroupBuilder MapTrackEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/generate", Generate);
        group.MapGet("/status/{jobId:guid}", GetStatus).DisableRateLimiting();
        group.MapGet("/download/{trackId:guid}",DownloadTrackAsync).DisableRateLimiting();
        return group;
    }

    private static async Task<IResult> Generate(GenerationRequestDto requestDto, JobStore jobStore, GenerationJobQueue jobQueue, ChordSymbolParser chordSymbolParser,  ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(nameof(TrackEndpoints));
        
        ValidationProblemDetails? validationProblemDetails = requestDto.Validate(chordSymbolParser);
        if (validationProblemDetails != null)
            return Results.BadRequest(validationProblemDetails);

        GenerationJob job = new() 
            { 
                Id = Guid.NewGuid(), 
                State = JobStates.Pending, 
                StartTime = DateTimeOffset.UtcNow,
                Mode = Enum.Parse<GenerationMode>(requestDto.Mode!),
                Tempo = requestDto.Tempo,
                Chords = requestDto.Chords!,
                KeyRoot = requestDto.KeyRoot!,
                Style = requestDto.Style!,
                TimeSignatureBeatCount = requestDto.TimeSignatureBeatCount,
                TimeSignatureBeatUnit = requestDto.TimeSignatureBeatUnit,
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

    private static async Task<IResult> DownloadTrackAsync(Guid trackId, IOptions<TrackStorageSettings> trackStorageSettings)
    {
        TrackStorageSettings storageSettings = trackStorageSettings.Value;

        DirectoryInfo directoryInfo = new(storageSettings.BaseFolderPath);
        if(!directoryInfo.Exists)
            throw new InvalidOperationException("Track storage not set correctly");

        FileInfo[] matches = directoryInfo.GetFiles($"{trackId}.*");
        if (matches.Length == 0)
            return Results.NotFound();

        FileInfo file = matches[0];
        string contentType = file.Extension.ToLowerInvariant() switch                                                                                                                                                  
        {                                
            ".json" => "application/json",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",                                                                                                                                                                                     
            ".ogg" => "audio/ogg",
            ".aac" => "audio/aac",
            _ => "application/octet-stream"
        };                                                                                                                                                                                                             
   
        Stream stream = file.OpenRead();                                                                                                                                                                               
        return Results.Stream(stream, contentType, enableRangeProcessing: true);   
    }
}