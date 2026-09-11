using BackingTrack.Api.Auth;
using BackingTrack.Api.Endpoints;
using BackingTrack.Api.Errors;
using BackingTrack.Api.Extensions;
using BackingTrack.Api.Serialization;
using BackingTrack.Generation;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackPersistence;
using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.TrackValidation;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddAppOptions(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Minimal APIs only throw on JSON body-binding failures when IsDevelopment() by default;
// force it in all environments so GlobalExceptionHandler sees a consistent BadHttpRequestException
// rather than a bare status-only 400 in Production.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// Observability
builder.AddObservability();

// Rate limiting
builder.Services.AddRateLimits(builder.Configuration);

// Job Processing
builder.Services.AddJobProcessing();

// Vector Store Initialisation
builder.Services.AddKnowledgeBase(builder.Configuration);

// Generation Workflow
builder.Services.AddLlmChatClient(builder.Configuration);
builder.Services.AddKnowledgeRetrieval();
builder.Services.AddTrackGeneration(builder.Configuration);
builder.Services.AddTrackValidation();
builder.Services.AddTrackRepair(builder.Configuration);
builder.Services.AddCritic(builder.Configuration);
builder.Services.AddTrackPersistence(builder.Configuration);
builder.Services.AddTrackGenerationWorkflow();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapApplicationHealthChecks();

_ = app.MapGroup("/tracks")
    .AddEndpointFilter<ApiKeyFilter>()
    .RequireRateLimiting("fixed")
    .MapTrackEndpoints();

app.Run();