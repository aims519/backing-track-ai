using BackingTrack.Core.Config;
using BackingTrack.Generation.Eval;
using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Eval.Reporting;
using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.KnowledgeBase;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

EvalHarnessArgs harnessArgs;
try
{
    harnessArgs = EvalHarnessArgs.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(EvalHarnessArgs.HelpText);
    return 2;
}

if (harnessArgs.ShowHelp)
{
    Console.WriteLine(EvalHarnessArgs.HelpText);
    return 0;
}

string goldenSetDir = harnessArgs.GoldenSetDirectory
    ?? Path.Combine(AppContext.BaseDirectory, "GoldenSet");

List<EvalCase> cases = GoldenSetLoader.Load(goldenSetDir).ToList();
if (harnessArgs.Categories.Count > 0)
    cases = cases.Where(c => harnessArgs.Categories.Contains(c.Category)).ToList();
if (harnessArgs.CaseIds.Count > 0)
    cases = cases.Where(c => harnessArgs.CaseIds.Contains(c.Id)).ToList();

if (cases.Count == 0)
{
    Console.Error.WriteLine("No cases matched the given filters.");
    return 2;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
// Resolve config relative to the binary, not the current directory, so `dotnet run` and a
// direct invocation of the built exe behave the same.
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
// The generation pipeline is chatty at Information; mute it below Warning unless -v is given.
builder.Logging.AddFilter("BackingTrack.Generation", harnessArgs.Verbose ? LogLevel.Debug : LogLevel.Warning);

builder.Services.AddGenerationEvalHarness(builder.Configuration);

using IHost host = builder.Build();

// Starts hosted services — notably VectorStoreWarmup, so the first case isn't billed for the
// cold vector-store load.
await host.StartAsync();

try
{
    // The harness bypasses the API's request validation, so check the golden set the way
    // /generate would before spending any tokens on it.
    ChordSymbolParser chordSymbolParser = host.Services.GetRequiredService<ChordSymbolParser>();
    IReadOnlyList<string> inputErrors = GoldenSetInputValidator.Validate(cases, chordSymbolParser);

    if (harnessArgs.DryRun)
    {
        Console.WriteLine($"{cases.Count} case(s) from {goldenSetDir}:");
        foreach (EvalCase c in cases)
            Console.WriteLine($"  {c.Id,-30} {c.Category,-20} {c.Description}");
    }

    if (inputErrors.Count > 0)
    {
        Console.Error.WriteLine($"{Environment.NewLine}Golden set has malformed inputs - fix these before running:");
        foreach (string error in inputErrors)
            Console.Error.WriteLine($"  {error}");
        return 2;
    }

    if (harnessArgs.DryRun)
        return 0;

    using CancellationTokenSource cts = new();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    EvalRunner runner = host.Services.GetRequiredService<EvalRunner>();
    IReadOnlyList<CaseOutcome> outcomes = await runner.RunAsync(cases, cts.Token);

    string trackStorageDirectory = Path.GetFullPath(
        host.Services.GetRequiredService<IOptions<TrackStorageSettings>>().Value.BaseFolderPath);
    RunReport report = RunReport.From(outcomes, trackStorageDirectory);
    ConsoleReporter.Print(report);

    if (harnessArgs.JsonReportPath is { } jsonPath)
    {
        await JsonReportWriter.WriteAsync(jsonPath, report, cts.Token);
        Console.WriteLine($"JSON report: {Path.GetFullPath(jsonPath)}");
    }

    return report.IsPass ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 2;
}
finally
{
    await host.StopAsync();
}