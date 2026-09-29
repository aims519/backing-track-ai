using BackingTrack.Core.Config;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackPersistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Tests.Config;

// Exercises the registrations end to end (bind + validate) rather than the generated validators
// directly, so a missing IValidateOptions<T> registration fails here too.
public class SettingsValidationTests
{
    private static readonly Dictionary<string, string?> ValidOpenAi = new()
    {
        ["Llm:Provider"] = "OpenAi",
        ["OpenAi:BaseUrl"] = "https://api.openai.com/v1",
        ["OpenAi:ApiKey"] = "key",
        ["OpenAi:GenerationModel"] = "gen",
        ["OpenAi:RepairModel"] = "repair",
        ["OpenAi:CriticModel"] = "critic",
        ["OpenAi:GenerationMaxOutputTokens"] = "1000",
        ["OpenAi:RepairMaxOutputTokens"] = "1000",
        ["OpenAi:CriticMaxOutputTokens"] = "1000",
    };

    private static T Resolve<T>(Action<IServiceCollection, IConfiguration> register, Dictionary<string, string?> values) where T : class
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ServiceCollection services = new();
        register(services, configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<T>>().Value;
    }

    [Fact]
    public void TrackStorage_Valid_Binds()
    {
        TrackStorageSettings settings = Resolve<TrackStorageSettings>(
            (s, c) => s.AddTrackPersistence(c), new() { ["TrackStorage:BaseFolderPath"] = "/tmp/tracks" });

        Assert.Equal("/tmp/tracks", settings.BaseFolderPath);
    }

    [Fact]
    public void TrackStorage_BlankBaseFolderPath_Throws()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve<TrackStorageSettings>(
            (s, c) => s.AddTrackPersistence(c), new() { ["TrackStorage:BaseFolderPath"] = " " }));
    }

    [Theory]
    [InlineData("-1", "3")]
    [InlineData("101", "3")]
    [InlineData("100", "0")]
    public void Critic_OutOfRange_Throws(string threshold, string maxAttempts)
    {
        Dictionary<string, string?> values = new(ValidOpenAi)
        {
            ["Critic:PassingScoreThreshold"] = threshold,
            ["Critic:MaxAttempts"] = maxAttempts,
        };

        Assert.Throws<OptionsValidationException>(() => Resolve<CriticSettings>((s, c) => s.AddCritic(c), values));
    }

    [Fact]
    public void Critic_Valid_Binds()
    {
        Dictionary<string, string?> values = new(ValidOpenAi)
        {
            ["Critic:PassingScoreThreshold"] = "100",
            ["Critic:MaxAttempts"] = "3",
        };

        CriticSettings settings = Resolve<CriticSettings>((s, c) => s.AddCritic(c), values);

        Assert.Equal(3, settings.MaxAttempts);
    }

    [Fact]
    public void OpenAi_Valid_Binds()
    {
        OpenAiSettings settings = Resolve<OpenAiSettings>((s, c) => s.AddLlmChatClient(c), ValidOpenAi);

        Assert.Equal("gen", settings.GenerationModel);
    }

    [Theory]
    [InlineData("OpenAi:ApiKey", "")]
    [InlineData("OpenAi:CriticModel", " ")]
    [InlineData("OpenAi:BaseUrl", "not a url")]
    [InlineData("OpenAi:RepairMaxOutputTokens", "0")]
    public void OpenAi_InvalidValue_Throws(string key, string value)
    {
        Dictionary<string, string?> values = new(ValidOpenAi) { [key] = value };

        Assert.Throws<OptionsValidationException>(() => Resolve<OpenAiSettings>((s, c) => s.AddLlmChatClient(c), values));
    }

    [Fact]
    public void OpenAi_MissingApiKey_Throws()
    {
        Dictionary<string, string?> values = new(ValidOpenAi);
        values.Remove("OpenAi:ApiKey");

        Assert.Throws<OptionsValidationException>(() => Resolve<OpenAiSettings>((s, c) => s.AddLlmChatClient(c), values));
    }
}