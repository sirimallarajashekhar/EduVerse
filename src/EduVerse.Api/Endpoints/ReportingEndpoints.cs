using EduVerse.Application.Reporting;

namespace EduVerse.Api.Endpoints;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/reporting").RequireAuthorization();
        api.MapGet("/dashboard", async (Guid campusId, DateOnly from, DateOnly to,
            ReportingService service, CancellationToken ct) => await Read(async () =>
                Results.Ok(await service.Dashboard(campusId, from, to, ct))));
        api.MapGet("/monthly.csv", async (Guid campusId, DateOnly from, DateOnly to,
            ReportingService service, CancellationToken ct) => await Read(async () =>
            {
                var data = await service.Dashboard(campusId, from, to, ct);
                return Results.File(ReportingService.Csv(data), "text/csv; charset=utf-8",
                    $"eduverse-report-{campusId:N}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
            }));
        return app;
    }
    private static async Task<IResult> Read(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (ReportingScopeException) { return Results.NotFound(); }
        catch (ArgumentException ex) { return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["reporting"] = [ex.Message] }); }
    }
}
