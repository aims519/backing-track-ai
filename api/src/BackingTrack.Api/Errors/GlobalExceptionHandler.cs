using System.Text.Json;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api.Errors;

internal class GlobalExceptionHandler(IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            string field = (badHttpRequestException.InnerException as JsonException)?.Path?.TrimStart('$', '.') is { Length: > 0 } path
                ? path
                : "Body";

            ValidationProblemDetails problemDetails = new()
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            };
            problemDetails.Errors[field] = ["The request body is missing or could not be parsed."];

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problemDetails
            });
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        ProblemDetails serverErrorDetails = new()
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred"
        };
        
        if(environment.IsDevelopment())
            serverErrorDetails.Detail = exception.ToString();
            
        logger.LogError(exception, "An unexpected error occurred");
        
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = serverErrorDetails
        });
    }
}