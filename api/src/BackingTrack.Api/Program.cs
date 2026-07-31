using System.Text.Json.Serialization;

using BackingTrack.Api.Auth;
using BackingTrack.Api.Errors;
using BackingTrack.Core.Config;

using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddOptions();
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

WebApplication app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")));

RouteGroupBuilder tracks = app.MapGroup("/tracks")
    .AddEndpointFilter<ApiKeyFilter>()
    .MapTrackEndpoints();

app.Run();

internal record HealthResponse(string Status);

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProblemDetails))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
