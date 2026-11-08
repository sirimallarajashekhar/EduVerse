using EduVerse.Api.Security;
using EduVerse.Application.Reporting;

namespace EduVerse.Api.Endpoints;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/reporting").RequireAuthorization().RequireModuleActions("reporting.read","reporting.read");
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
        api.MapGet("/monthly.xlsx", async (Guid campusId, DateOnly from, DateOnly to,
            ReportingService service, CancellationToken ct) => await Read(async () =>
            {
                var report = await service.Dashboard(campusId, from, to, ct);
                return Results.File(ReportDocumentWriter.Xlsx(report),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"eduverse-report-{campusId:N}.xlsx");
            }));
        api.MapGet("/monthly.pdf", async (Guid campusId, DateOnly from, DateOnly to,
            ReportingService service, CancellationToken ct) => await Read(async () =>
            {
                var report = await service.Dashboard(campusId, from, to, ct);
                return Results.File(ReportDocumentWriter.Pdf(report), "application/pdf",
                    $"eduverse-report-{campusId:N}.pdf");
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
