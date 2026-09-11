using BackingTrack.Critic.Eval;
using BackingTrack.Critic.Eval.Fixtures;
using BackingTrack.Critic.Eval.Reporting;
using BackingTrack.Critic.Eval.Running;

using BackingTrack.Generation;
using BackingTrack.Generation.Steps.Critic;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

EvalArgs evalArgs;
try
{
    evalArgs = EvalArgs.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(EvalArgs.HelpText);
    return 2;
}

if (evalArgs.ShowHelp)
{
    Console.WriteLine(EvalArgs.HelpText);
    return 0;
}

List<CriticFixture> fixtures = CriticFixtures.All.ToList();
if (evalArgs.FixtureIds.Count > 0)
    fixtures = fixtures.Where(f => evalArgs.FixtureIds.Contains(f.Id)).ToList();

if (fixtures.Count == 0)
{
    Console.Error.WriteLine("No fixtures matched the given --id filter(s).");
    return 2;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
builder.Configuration.AddUserSecrets<EvalArgs>(optional: true);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
// The generation pipeline's LLM client is chatty at Debug (request/response bodies) - mute it
// below Information unless -v is given.
builder.Logging.AddFilter("BackingTrack.Generation", evalArgs.Verbose ? LogLevel.Debug : LogLevel.Information);

// Just enough of the generation pipeline's DI to construct ICriticStep - no knowledge base,
// retrieval, generation or persistence wiring, since fixtures supply their own matched
// style/chords/scales directly rather than going through retrieval.
builder.Services.AddOptions();
builder.Services.AddLlmChatClient(builder.Configuration);
builder.Services.AddCritic(builder.Configuration);
builder.Services.AddSingleton<CriticEvalRunner>();

using IHost host = builder.Build();

Console.WriteLine($"Running {fixtures.Count} fixture(s) x {evalArgs.RepeatCount} repeat(s) against the live critic model - this costs tokens.");
Console.WriteLine();

CriticEvalRunner runner = host.Services.GetRequiredService<CriticEvalRunner>();
using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

try
{
    IReadOnlyList<FixtureOutcome> outcomes = await runner.RunAsync(fixtures, evalArgs.RepeatCount, cts.Token);
    bool allCorrectAndConsistent = ConsoleReporter.Print(outcomes);
    return allCorrectAndConsistent ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 2;
}
