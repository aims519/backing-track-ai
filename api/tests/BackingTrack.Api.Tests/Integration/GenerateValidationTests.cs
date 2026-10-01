using System.Net;
using System.Net.Http.Json;

using BackingTrack.Api.Dtos;

using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api.Tests.Integration;

// Every generate request counts against the rate limit even when it is rejected, so each test
// builds its own ApiFactory rather than sharing a limiter through a class fixture.
public class GenerateValidationTests
{
    [Fact]
    public async Task Missing_style_is_a_validation_problem()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        GenerationRequestDto request = TestRequests.ValidGenerateRequest() with { Style = null };

        HttpResponseMessage response = await client.PostGenerateAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Contains("Style", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Out_of_range_tempo_is_a_validation_problem()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        GenerationRequestDto request = TestRequests.ValidGenerateRequest() with { Tempo = 500 };

        HttpResponseMessage response = await client.PostGenerateAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Contains("Tempo", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Malformed_json_is_a_bad_request_problem_not_a_server_error()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostRawJsonAsync("/tracks/generate", "{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.NotEmpty(problem!.Errors);
    }

    [Fact]
    public async Task Missing_body_is_a_bad_request()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsync("/tracks/generate", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}