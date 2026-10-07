using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackPersistence;
using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.TrackValidation;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackingTrack.Generation.Eval;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Everything the harness needs to run the workflow, kept out of <c>Program.cs</c> so a test
    /// can build the same container and catch a missing registration without a live run.
    /// </summary>
    public static IServiceCollection AddGenerationEvalHarness(this IServiceCollection services, IConfiguration configuration)
    {
        // Generation pipeline — mirrors BackingTrack.Api/Program.cs, minus the web host, observability,
        // rate limiting and the background job worker. The harness runs the workflow directly through
        // WorkflowGenerationRunner, so AddJobProcessing() is deliberately not wired.
        services.AddOptions();
        services.AddKnowledgeBase(configuration);
        services.AddLlmChatClient(configuration);
        services.AddKnowledgeRetrieval();
        services.AddTrackGeneration(configuration);

        // Wrap the real generator behind a fault-injecting decorator so Fallback cases can simulate an
        // LLM outage (the generate call throwing) — unlike the validator and critic, no input can
        // provoke that from the real model. Only throws when a case sets
        // EvalCase.ForceGenerateExceptions > 0; every other case still exercises the real generator.
        services.AddTransient<GenerateStep>();
        services.AddScoped<GenerateFaultInjectionState>();
        services.Replace(ServiceDescriptor.Transient<IGenerateStep, FaultInjectingGenerateStep>());

        services.AddTrackValidation();

        // Wrap the real validator behind a fault-injecting decorator so Fallback cases can force
        // deterministic repair-budget exhaustion instead of hoping a "hard" input draws enough genuine
        // validation failures from the model. Only forces anything when a case sets
        // EvalCase.ForceValidationFailures > 0 — every other case still exercises the real validator
        // untouched.
        services.AddTransient<ValidateStep>();
        services.AddScoped<ValidationFaultInjectionState>();
        services.Replace(ServiceDescriptor.Transient<IValidateStep, FaultInjectingValidateStep>());

        services.AddTrackRepair(configuration);
        services.AddTrackPersistence(configuration);
        services.AddCritic(configuration);
        services.AddFallbackTrackGeneration();
        services.AddTrackGenerationWorkflow();

        // The executors report progress through IJobStatusEmitter, whose real implementation writes to
        // the JobStore that AddJobProcessing() registers. Without the job store there's nothing to
        // report against, so discard it.
        services.AddSingleton<IJobStatusEmitter, NoOpJobStatusEmitter>();

        // Wrap the real critic behind a fault-injecting decorator so CriticTriggering cases can force a
        // deterministic rejection instead of hoping the input draws a genuine one from the model. Only
        // forces anything when a case sets EvalCase.ForceCriticRejections > 0 — every other case still
        // exercises the real critic untouched.
        services.AddTransient<CriticStep>();
        services.AddScoped<CriticFaultInjectionState>();
        services.Replace(ServiceDescriptor.Transient<ICriticStep, FaultInjectingCriticStep>());

        services.AddSingleton<IGenerationRunner, WorkflowGenerationRunner>();
        services.AddSingleton<EvalRunner>();

        return services;
    }
}