using System.Net;
using System.Text;

using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.KnowledgeBase.Embedder.VoyageAi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.KnowledgeBase.Embedder.Tests;

/// <summary>
/// Exercises the retry behaviour through the real DI registration (<see cref="ServiceCollectionExtensions.AddVoyageAiEmbedding"/>),
/// with only the primary message handler swapped for a stub, so it covers whatever resilience pipeline that registration adds.
/// Deliberately doesn't assert exact retry counts or delays, so tuning the pipeline doesn't break these.
/// </summary>
public class VoyageAiEmbeddingServiceRetryTests
{
    private const string SuccessBody = """{"data":[{"index":0,"embedding":[0.1,0.2]},{"index":1,"embedding":[0.3,0.4]}]}""";

    private static readonly IReadOnlyList<IEmbeddable> Embeddables =
    [
        new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" },
        new ChordEntry { Id = "min", Intervals = [0, 3, 7], Description = "A minor triad" }
    ];

    [Fact]
    public async Task EmbedBatchAsync_SuccessOnFirstAttempt_CallsVoyageOnce()
    {
        StubHttpMessageHandler handler = new(Success());

        List<(IEmbeddable Embeddable, float[] EmbedVector)> results = await EmbedAsync(handler);

        AssertExpectedVectors(results);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task EmbedBatchAsync_TransientStatusThenSuccess_RetriesAndReturnsEmbeddings(HttpStatusCode transientStatus)
    {
        StubHttpMessageHandler handler = new(Status(transientStatus), Success());

        List<(IEmbeddable Embeddable, float[] EmbedVector)> results = await EmbedAsync(handler);

        AssertExpectedVectors(results);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task EmbedBatchAsync_MultipleTransientFailuresThenSuccess_KeepsRetrying()
    {
        StubHttpMessageHandler handler = new(
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.TooManyRequests),
            Success());

        List<(IEmbeddable Embeddable, float[] EmbedVector)> results = await EmbedAsync(handler);

        AssertExpectedVectors(results);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task EmbedBatchAsync_NetworkErrorThenSuccess_RetriesAndReturnsEmbeddings()
    {
        StubHttpMessageHandler handler = new(
            () => throw new HttpRequestException("Connection reset"),
            Success());

        List<(IEmbeddable Embeddable, float[] EmbedVector)> results = await EmbedAsync(handler);

        AssertExpectedVectors(results);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task EmbedBatchAsync_PersistentTransientFailure_GivesUpAndThrowsAfterRetrying()
    {
        // Repeats the last response forever, so only the pipeline's own retry limit can end the call.
        StubHttpMessageHandler handler = new(Status(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAnyAsync<Exception>(() => EmbedAsync(handler));

        Assert.True(handler.CallCount > 1, $"Expected the call to be retried, but Voyage was only called {handler.CallCount} time(s)");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task EmbedBatchAsync_NonTransientStatus_DoesNotRetry(HttpStatusCode status)
    {
        StubHttpMessageHandler handler = new(Status(status), Success());

        await Assert.ThrowsAnyAsync<Exception>(() => EmbedAsync(handler));

        Assert.Equal(1, handler.CallCount);
    }

    private static void AssertExpectedVectors(List<(IEmbeddable Embeddable, float[] EmbedVector)> results)
    {
        Assert.Equal(["maj", "min"], results.Select(r => r.Embeddable.Id));
        Assert.Equal([0.1f, 0.2f], results[0].EmbedVector);
        Assert.Equal([0.3f, 0.4f], results[1].EmbedVector);
    }

    private static async Task<List<(IEmbeddable Embeddable, float[] EmbedVector)>> EmbedAsync(StubHttpMessageHandler handler)
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

        services.AddVoyageAiEmbedding(configuration);

        // A typed client is named after its service type, and extra configuration for a name accumulates with the last
        // primary handler winning. So this replaces the network layer while leaving any resilience handler added by
        // AddVoyageAiEmbedding in place above it.
        services.AddHttpClient(nameof(IEmbeddingService)).ConfigurePrimaryHttpMessageHandler(() => handler);

        await using ServiceProvider provider = services.BuildServiceProvider();
        IEmbeddingService service = provider.GetRequiredService<IEmbeddingService>();

        Task<List<(IEmbeddable Embeddable, float[] EmbedVector)>> call = CollectAsync(service);

        // Backoff delays wait on the fake clock, so keep nudging it forward until the call settles.
        while (!call.IsCompleted)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);
        }

        return await call;
    }

    private static async Task<List<(IEmbeddable Embeddable, float[] EmbedVector)>> CollectAsync(IEmbeddingService service)
    {
        List<(IEmbeddable Embeddable, float[] EmbedVector)> results = [];
        await foreach ((IEmbeddable Embeddable, float[] EmbedVector) item in service.EmbedBatchAsync(Embeddables))
            results.Add(item);

        return results;
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