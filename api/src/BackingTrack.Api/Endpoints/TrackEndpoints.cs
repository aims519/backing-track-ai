internal static class TrackEndpoints
{
    public static RouteGroupBuilder MapTrackEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/generate", Generate);
        return group;
    }

    private static async Task<IResult> Generate()
    {
        throw new NotImplementedException();
    }
}
