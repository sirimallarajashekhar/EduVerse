using EduVerse.Application.Workflows;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

public static class CrossModuleEndpoints
{
    public static IEndpointRouteBuilder MapCrossModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/workflows").RequireAuthorization();
        api.MapGet("/employees/{employee:guid}/clearance", async
            (Guid employee, Guid campusId, CrossModuleService service, CancellationToken ct) =>
            await Handle(() => service.Clearance(campusId, employee, ct)));
        api.MapPost("/employees/bind-identity", async
            (EmployeeWorkflowRequest request, HttpContext http, CrossModuleService service, CancellationToken ct) =>
            await Handle(() => service.Bind(request, http.Request.Headers["Idempotency-Key"].ToString(), ct)));
        api.MapPost("/employees/offboard", async
            (EmployeeWorkflowRequest request, HttpContext http, CrossModuleService service, CancellationToken ct) =>
            await Handle(() => service.Offboard(request, http.Request.Headers["Idempotency-Key"].ToString(), ct)));
        api.MapPost("/procurement/submit-finance", async
            (PurchaseWorkflowRequest request, HttpContext http, CrossModuleService service, CancellationToken ct) =>
            await Handle(() => service.RequestFinance(request, http.Request.Headers["Idempotency-Key"].ToString(), ct)));
        return app;
    }
    private static async Task<IResult> Handle<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (WorkflowScopeException) { return Results.NotFound(); }
        catch (WorkflowConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex) { return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["workflow"] = [ex.Message] }); }
        catch (MySqlException ex) when (ex.Number == 1062) { return Results.Conflict(
            new { message = "This workflow has already been recorded." }); }
    }
}
