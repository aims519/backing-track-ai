using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Workflows;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Eval.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenerationEvalHarness_WithHarnessConfig_ResolvesEverythingTheRunNeeds()
    {
        // The harness's own appsettings.json (copied here via the project reference), so a
        // missing config section fails this test too. Its placeholder API keys are enough:
        // nothing here makes a network call.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddGenerationEvalHarness(configuration);

        // ValidateOnBuild checks every registration's constructor dependencies up front, which is
        // what the missing IJobStatusEmitter would have failed.
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<EvalRunner>());

        using IServiceScope scope = provider.CreateScope();
        Assert.NotNull(GenerationWorkflowBuilder.Build(scope.ServiceProvider));
    }
}