using System.Collections.Concurrent;
using System.Net;

using BackingTrack.Api.Extensions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.Api.Tests.Integration;

// Fresh ApiFactory per test: each one exhausts a limiter, and that state must not leak between tests.
public class RateLimitLoggingTests
{
    [Fact]
    public async Task Rejection_log_names_the_route_template_not_the_requested_path()
    {
        CapturingLoggerProvider logs = new();
        using ApiFactory factory = new();
        using WebApplicationFactory<Program> loggedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using HttpClient client = loggedFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ApiKey);
        for (int i = 0; i < ApiFactory.RateLimitPermits; i++)
            await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        // Route matching is case-insensitive, so this still hits the limited endpoint, but the raw
        // path differs from the template. If the log used Request.Path, the casing would show up.
        HttpResponseMessage response = await client.PostRawJsonAsync("/TRACKS/Generate", "{}");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        string message = Assert.Single(logs.Messages, m => m.StartsWith("Rate limit exceeded", StringComparison.Ordinal));
        Assert.EndsWith("on /tracks/generate", message);
        Assert.DoesNotContain("/TRACKS/Generate", message);
    }

    [Fact]
    public void Route_template_falls_back_to_unknown_when_no_endpoint_matched()
    {
        Assert.Equal("unknown", RateLimitingExtensions.GetRouteTemplate(new DefaultHttpContext()));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) =>
            categoryName == "RateLimiting" ? new CapturingLogger(_messages) : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}