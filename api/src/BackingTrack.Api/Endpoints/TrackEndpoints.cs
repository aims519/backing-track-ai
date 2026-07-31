internal static class TrackEndpoints
{
    public static RouteGroupBuilder MapTrackEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/generate", Generate);
        return group;
    }

    private static async Task<IResult> Generate(ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(nameof(TrackEndpoints));
        logger.LogInformation("Generating backing track...");
        return Results.Ok();
    }
}
