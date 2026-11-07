using System.Security.Claims;
using EduVerse.Api.Security;
using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity");
        group.MapPost("/login", async (LoginRequest request, IdentityService service, CancellationToken ct) =>
        {
            var result = await service.LoginAsync(request.TenantSlug, request.Email, request.Password, ct);
            return result is null
                ? Results.Problem(statusCode: 401, title: "Invalid credentials or institution")
                : Results.Ok(new LoginResponse(result.AccessToken, result.ExpiresUtc, result.DisplayName, result.Email, result.UserId.ToString("D"), result.TenantId, result.RoleCode));
        }).RequireRateLimiting("auth");

        group.MapPost("/password-reset/request", async (PasswordResetRequest request,
            IdentityService service, ILoggerFactory loggers, CancellationToken ct) =>
        {
            try { await service.RequestResetAsync(request.Email, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Same response whether account exists or SMTP is unavailable. Never log tokens.
                loggers.CreateLogger("Identity").LogError("Password reset notification failed.");
            }
            return Results.Accepted();
        }).RequireRateLimiting("auth");

        group.MapPost("/password-reset/confirm", async (PasswordResetConfirm request,
            IdentityService service, CancellationToken ct) =>
        {
            var ok = await service.ConfirmResetAsync(request.Token, request.Password, ct);
            return ok ? Results.NoContent() : Results.Problem(statusCode: 400, title: "Invalid or expired reset token/password");
        }).RequireRateLimiting("auth");

        var protectedGroup = group.MapGroup("").RequireAuthorization();
        protectedGroup.MapGet("/session", () => Results.NoContent());
        protectedGroup.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var tenant = user.FindFirst("tenant_id")?.Value;
            var subject = user.FindFirst("sub")?.Value;
            return Guid.TryParse(tenant, out var id) && !string.IsNullOrEmpty(subject)
                ? Results.Ok(new { subjectId = subject, tenantId = id })
                : Results.Problem(statusCode: 403, title: "Invalid identity claims");
        });

        protectedGroup.MapPost("/logout", async (ClaimsPrincipal user, IdentityService service, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sid")?.Value, out var sid) ||
                !Guid.TryParse(user.FindFirst("sub")?.Value, out var uid) ||
                !Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tid))
                return Results.Problem(statusCode: 403, title: "Invalid session claims");
            await service.LogoutAsync(sid, uid, tid, ct);
            return Results.NoContent();
        });

        protectedGroup.MapGet("/users", async (ITenantContext context, IdentityService service, CancellationToken ct) =>
        {
            if (!context.TryGet(out var tenant) || tenant is null) return Results.Forbid();
            try { return Results.Ok(await service.UsersAsync(tenant, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });
        protectedGroup.MapPost("/users", async (ITenantContext context, UserCreateRequest request,
            IdentityService service, CancellationToken ct) =>
        {
            if (!context.TryGet(out var tenant) || tenant is null) return Results.Forbid();
            try
            {
                await service.InviteAsync(tenant, request.Email, request.DisplayName, request.RoleCode, ct);
                return Results.Accepted();
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException) { return Results.BadRequest(new { message = "Invalid account details." }); }
            catch (MySqlException ex) when (ex.Number == 1062)
            { return Results.Conflict(new { message = "User already belongs to this institution." }); }
        });
        protectedGroup.MapPost("/users/{userId:guid}/deactivate",
            async (Guid userId, ITenantContext context, IdentityService service, CancellationToken ct) =>
            {
                if (!context.TryGet(out var tenant) || tenant is null) return Results.Forbid();
                try
                {
                    return await service.DeactivateMemberAsync(tenant, userId, ct)
                        ? Results.NoContent()
                        : Results.NotFound();
                }
                catch (UnauthorizedAccessException) { return Results.Forbid(); }
            });

        protectedGroup.MapPost("/permissions", async (ITenantContext context, PermissionGrantRequest request,
            IdentityService service, CancellationToken ct) =>
        {
            if (!context.TryGet(out var tenant) || tenant is null) return Results.Forbid();
            if (request.RoleCode != "MEMBER") return Results.BadRequest(new { message = "Only MEMBER grants are configurable." });
            try
            {
                await service.GrantPermissionAsync(tenant, request.PermissionCode, ct);
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException) { return Results.BadRequest(new { message = "Unknown permission." }); }
        });
        protectedGroup.MapPost("/campus-access", async (ITenantContext context, CampusGrantRequest request,
            IdentityService service, CancellationToken ct) =>
        {
            if (!context.TryGet(out var tenant) || tenant is null) return Results.Forbid();
            try
            {
                return await service.GrantCampusAsync(tenant, request.UserId, request.CampusId, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });

        protectedGroup.MapGet("/permissions/check", async (ITenantContext context,
            string permission, Guid? campusId, IdentityService service, CancellationToken ct) =>
        {
            if (!context.TryGet(out var tenant) || tenant is null) return Results.Forbid();
            if (!KnownPermissions.All.Contains(permission)) return Results.BadRequest();
            var granted = await service.AllowedAsync(tenant, permission, campusId, ct);
            return Results.Ok(new { allowed = granted });
        });
        return app;
    }
}
