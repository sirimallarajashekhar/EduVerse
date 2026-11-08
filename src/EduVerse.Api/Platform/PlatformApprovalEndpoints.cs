using System.Security.Claims;
using EduVerse.Application.Identity;
using EduVerse.Api.Security;
using EduVerse.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;

namespace EduVerse.Api.Platform;

/// <summary>Only authenticated, DB-verified platform operators may review registrations.</summary>
public static class PlatformApprovalEndpoints
{
    public static IEndpointRouteBuilder MapPlatformApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/platform-auth/login", async (
            PlatformLoginRequest request,PlatformApprovalStore store,IAccessTokenIssuer issuer,CancellationToken ct) =>
        {
            var session=await store.LoginAsync(request.Email,request.Password,ct);
            if(session is null) return Results.Problem(statusCode:401,title:"Invalid credentials");
            var token=issuer.Issue(session.UserId,Guid.Empty,session.SessionId,
                "PLATFORM_SUPER_ADMIN",session.ExpiresUtc);
            return Results.Ok(new LoginResponse(token,session.ExpiresUtc,session.DisplayName,
                session.Email,session.UserId.ToString("D"),Guid.Empty,"PLATFORM_SUPER_ADMIN"));
        }).RequireRateLimiting("auth");

        var group=app.MapGroup("/api/v1/platform-admin")
            .RequireAuthorization(policy => policy.RequireRole("PLATFORM_SUPER_ADMIN"));

        // JWT middleware checks the live platform operator session before this handler runs.
        // The Web SSO cookie must not validate a platform token against a tenant session.
        group.MapGet("/session",()=>Results.NoContent());

        group.MapGet("/registrations",async (string? status,PlatformApprovalStore store,CancellationToken ct) =>
        {
            try { return Results.Ok(await store.ListAsync((status??"PENDING").ToUpperInvariant(),ct)); }
            catch(ArgumentException) { return Results.BadRequest(new { message="Invalid status" }); }
        });
        group.MapPost("/registrations/{id:guid}/approve",async (
            Guid id,ReviewCommand request,ClaimsPrincipal user,PlatformApprovalStore store,CancellationToken ct) =>
            await Review(id,user,request,store,true,ct));
        group.MapPost("/registrations/{id:guid}/reject",async (
            Guid id,ReviewCommand request,ClaimsPrincipal user,PlatformApprovalStore store,CancellationToken ct) =>
            await Review(id,user,request,store,false,ct));
        group.MapPost("/logout",async (ClaimsPrincipal user,PlatformApprovalStore store,CancellationToken ct) =>
        {
            if(!Guid.TryParse(user.FindFirst("sub")?.Value,out var uid) ||
               !Guid.TryParse(user.FindFirst("sid")?.Value,out var sid))return Results.Forbid();
            await store.LogoutAsync(uid,sid,ct);
            return Results.NoContent();
        });
        return app;
    }

    private static async Task<IResult> Review(Guid id,ClaimsPrincipal user,ReviewCommand request,
        PlatformApprovalStore store,bool approve,CancellationToken ct)
    {
        if(!Guid.TryParse(user.FindFirst("sub")?.Value,out var uid))return Results.Forbid();
        try
        {
            var result=await store.ReviewAsync(id,uid,approve,request.Reason,ct);
            return result is null ? Results.NotFound():Results.Ok(result);
        }
        catch(ArgumentException ex) {return Results.BadRequest(new { message=ex.Message });}
        catch(InvalidOperationException ex) {return Results.Conflict(new { message=ex.Message });}
        catch(MySqlException ex) when(ex.Number==1062)
        {return Results.Conflict(new { message="Institution slug is already assigned." });}
    }
}
public sealed record PlatformLoginRequest(string Email,string Password);
