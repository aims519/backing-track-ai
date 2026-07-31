using System.Security.Cryptography;
using System.Text;

using BackingTrack.Core.Config;

using Microsoft.Extensions.Options;

namespace BackingTrack.Api.Auth;

/// <summary>
/// A simple filter that checks the value of the inbound API key request header against the expected value
/// </summary>
internal sealed class ApiKeyFilter(IOptions<Core.Config.AuthSettings> authSettings) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string? apiKeyHeader = context.HttpContext.Request.Headers["X-API-Key"];
        if (string.IsNullOrEmpty(apiKeyHeader))
            return Results.Unauthorized();

        string expectedApiKey = authSettings.Value.ApiKey;
        if (string.IsNullOrEmpty(expectedApiKey))
            return Results.Unauthorized();
        
        bool bEqual = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKeyHeader), Encoding.UTF8.GetBytes(expectedApiKey));
        if (!bEqual)
            return Results.Unauthorized();
            
        return await next(context);
    }
}