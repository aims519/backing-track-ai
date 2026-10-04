using System.Net;
using System.Text;

using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.Generation.Tests.KnowledgeBase.Embedding;

/// <summary>
/// Exercises the retry behaviour through the real DI registration (<see cref="ServiceCollectionExtensions.AddKnowledgeBase"/>),
/// with only the primary message handler swapped for a stub, so it covers whatever resilience pipeline that registration adds.
/// Deliberately doesn't assert exact retry counts or delays, so tuning the pipeline doesn't break these.
/// </summary>
public class VoyageAiQueryEmbeddingServiceRetryTests
{
    private const string SuccessBody = """{"data":[{"index":0,"embedding":[0.25,0.5,0.75]}]}""";

    [Fact]
    public async Task EmbedQueryAsync_SuccessOnFirstAttempt_CallsVoyageOnce()
    {
        StubHttpMessageHandler handler = new(Success());

        float[] result = await EmbedAsync(handler);

        Assert.Equal([0.25f, 0.5f, 0.75f], result);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task EmbedQueryAsync_TransientStatusThenSuccess_RetriesAndReturnsEmbedding(HttpStatusCode transientStatus)
    {
        StubHttpMessageHandler handler = new(Status(transientStatus), Success());

        float[] result = await EmbedAsync(handler);

        Assert.Equal([0.25f, 0.5f, 0.75f], result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task EmbedQueryAsync_MultipleTransientFailuresThenSuccess_KeepsRetrying()
    {
        StubHttpMessageHandler handler = new(
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.TooManyRequests),
            Success());

        float[] result = await EmbedAsync(handler);

        Assert.Equal([0.25f, 0.5f, 0.75f], result);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task EmbedQueryAsync_NetworkErrorThenSuccess_RetriesAndReturnsEmbedding()
    {
        StubHttpMessageHandler handler = new(
            () => throw new HttpRequestException("Connection reset"),
            Success());

        float[] result = await EmbedAsync(handler);

        Assert.Equal([0.25f, 0.5f, 0.75f], result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task EmbedQueryAsync_PersistentTransientFailure_GivesUpAndThrowsAfterRetrying()
    {
        // Repeats the last response forever, so only the pipeline's own retry limit can end the call.
        StubHttpMessageHandler handler = new(Status(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmbedAsync(handler));

        Assert.True(handler.CallCount > 1, $"Expected the call to be retried, but Voyage was only called {handler.CallCount} time(s)");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task EmbedQueryAsync_NonTransientStatus_DoesNotRetry(HttpStatusCode status)
    {
        StubHttpMessageHandler handler = new(Status(status), Success());

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmbedAsync(handler));

        Assert.Equal(1, handler.CallCount);
    }

    private static async Task<float[]> EmbedAsync(StubHttpMessageHandler handler)
    {
        // A fake clock stops the backoff delays making the tests slow; Polly takes its clock from DI.
        FakeTimeProvider timeProvider = new();

        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddLogging();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VoyageAi:ApiKey"] = "test-key",
                ["VoyageAi:RestApiBaseUrl"] = "https://voyage.test/v1/",
                ["VoyageAi:EmbeddingModel"] = "test-model",
            })
            .Build();

        services.AddKnowledgeBase(configuration);

        // A typed client is named after its service type, and extra configuration for a name accumulates with the last
        // primary handler winning. So this replaces the network layer while leaving any resilience handler added by
        // AddKnowledgeBase in place above it.
        services.AddHttpClient(nameof(IQueryEmbeddingService)).ConfigurePrimaryHttpMessageHandler(() => handler);

        await using ServiceProvider provider = services.BuildServiceProvider();
        IQueryEmbeddingService service = provider.GetRequiredService<IQueryEmbeddingService>();

        Task<float[]> call = service.EmbedQueryAsync("Cmaj7");

        // Backoff delays wait on the fake clock, so keep nudging it forward until the call settles.
        while (!call.IsCompleted)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);
        }

        return await call;
    }

    private static Func<HttpResponseMessage> Success() => () => Response(HttpStatusCode.OK, SuccessBody);

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => Response(status, "{}");

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    /// <summary>Plays back the supplied responses in order, repeating the last one once they run out.</summary>
    private sealed class StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int index = Math.Min(Interlocked.Increment(ref _callCount) - 1, responses.Length - 1);
            return Task.FromResult(responses[index]());
        }
    }
}