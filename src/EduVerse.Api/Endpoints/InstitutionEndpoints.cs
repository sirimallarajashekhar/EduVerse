using EduVerse.Application.Tenancy;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

public sealed record CreateCampusRequest(string? Code, string? Name);

public static class InstitutionEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/institutions").RequireAuthorization();
        group.MapGet("/current", async (InstitutionService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.CurrentAsync(ct)); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403, title: "Tenant access denied"); }
        });
        group.MapGet("/campuses", async (InstitutionService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.CampusesAsync(ct)); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403, title: "Tenant access denied"); }
        });
        group.MapPost("/campuses", async (CreateCampusRequest request, InstitutionService service, CancellationToken ct) =>
        {
            try
            {
                if (request.Code is null || request.Name is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["campus"] = ["Code and name are required."]
                    });
                var campus = await service.CreateCampusAsync(request.Code, request.Name, ct);
                return Results.Created($"/api/v1/institutions/campuses/{campus.Id}", campus);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "campus"] = [ex.Message] });
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return Results.Conflict(new { message = "Campus code already exists in this institution." });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: 403, title: "Tenant access denied");
            }
        });
        return app;
    }
}
