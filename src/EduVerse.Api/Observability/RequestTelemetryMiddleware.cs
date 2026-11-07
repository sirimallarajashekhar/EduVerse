using System.Diagnostics;

namespace EduVerse.Api.Observability;

/// <summary>Emits trace correlation and latency without sensitive request bodies, headers or user identifiers.</summary>
public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var watch = Stopwatch.StartNew();
        context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
        try { await next(context); }
        finally
        {
            watch.Stop();
            logger.LogInformation("HTTP {Method} {Endpoint} -> {Status} in {DurationMs}ms; trace={Trace}",
                context.Request.Method,
                context.GetEndpoint()?.DisplayName ?? "unmatched",
                context.Response.StatusCode, watch.Elapsed.TotalMilliseconds,
                context.TraceIdentifier);
        }
    }
}
