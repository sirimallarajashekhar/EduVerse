using EduVerse.Application.Operations;
using EduVerse.Application.Tenancy;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

/// <summary>Explicit opt-in idempotent variant; existing /campuses remains backward compatible.</summary>
public static class IdempotentCampusEndpoints
{
    public sealed record Request(string? Code, string? Name);

    public static IEndpointRouteBuilder MapIdempotentCampusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/institutions/campuses/idempotent",
            async (Request request, HttpContext http, InstitutionService institutions,
                IIdempotentCampusWriter writer, ITenantContext context, CancellationToken ct) =>
            {
                if (!context.TryGet(out var identity) || identity is null)
                    return Results.Forbid();
                var key = http.Request.Headers["Idempotency-Key"].ToString();
                if (key.Length is < 8 or > 128 || key.Any(c => c is < '!' or > '~'))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Idempotency-Key"] = ["Provide an ASCII key of 8-128 non-space characters."] });
                if (request.Code is null || request.Name is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["campus"] = ["Code and name are required."] });
                try
                {
                    // Existing domain service owns campus validation and ADMIN membership check.
                    // This operation validates independently without creating an extra campus.
                    var info = await institutions.CurrentAsync(ct);
                    if (info.Id != identity.TenantId) return Results.Forbid();
                    var (code, name) = EduVerse.Application.Operations.CampusInput.Normalize(request.Code, request.Name);
                    // Must prove fresh ADMIN membership before writing; token role is insufficient.
                    if (!await http.RequestServices.GetRequiredService<IInstitutionRepository>()
                        .IsTenantAdminAsync(identity, ct)) return Results.Forbid();
                    var campus = await writer.CreateAsync(identity, code, name, key, ct);
                    return Results.Ok(campus);
                }
                catch (IdempotencyConflictException) { return Results.Conflict(new { message = "Idempotency key reused with different content." }); }
                catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["campus"] = [ex.Message] }); }
                catch (UnauthorizedAccessException) { return Results.Forbid(); }
                catch (MySqlException ex) when (ex.Number == 1062) { return Results.Conflict(new { message = "Campus code already exists." }); }
            }).RequireAuthorization();
        return app;
    }
}
