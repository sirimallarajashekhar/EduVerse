using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Api.Observability;

/// <summary>Safe RFC 7807 responses: no exception messages or secrets escape to the caller.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return false;
        var status = exception switch
        {
            BadHttpRequestException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogError(exception, "Unhandled API request; traceId={TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        await Results.Problem(
            statusCode: status,
            title: status == 400 ? "Invalid request" : "Unexpected server error",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return true;
    }
}
