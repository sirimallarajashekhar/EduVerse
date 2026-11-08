using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;

namespace EduVerse.Api.Platform;

/// <summary>Cross-tenant administration requires a live operator JWT, never a tenant ADMIN token.</summary>
public static class PlatformControlEndpoints
{
    public static IEndpointRouteBuilder MapPlatformControlEndpoints(this IEndpointRouteBuilder app)
    {
        var api=app.MapGroup("/api/v1/platform-control")
            .RequireAuthorization(policy=>policy.RequireRole("PLATFORM_SUPER_ADMIN"));
        api.MapGet("/institutions",async (PlatformControlStore store,CancellationToken ct)=>
            Results.Ok(await store.Institutions(ct)));
        api.MapGet("/institutions/{tenantId:guid}/modules",async (Guid tenantId,
            PlatformControlStore store,CancellationToken ct)=>
        {
            var result=await store.Modules(tenantId,ct);
            return result is null?Results.NotFound():Results.Ok(result);
        });
        api.MapGet("/institutions/{tenantId:guid}/users",async (Guid tenantId,
            PlatformControlStore store,CancellationToken ct)=>
        {
            var result=await store.Users(tenantId,ct);
            return result is null?Results.NotFound():Results.Ok(result);
        });
        api.MapPost("/institutions/{tenantId:guid}/users",async (Guid tenantId,
            PlatformInvite request,ClaimsPrincipal user,PlatformControlStore store,CancellationToken ct)=>
        {
            if(!Guid.TryParse(user.FindFirst("sub")?.Value,out var operatorId))return Results.Forbid();
            if(tenantId!=request.TenantId)return Results.BadRequest(new {message="Tenant mismatch."});
            try{return await store.Invite(operatorId,request,ct)?Results.Accepted():Results.NotFound();}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
            catch(MySqlException ex) when(ex.Number==1062)
              {return Results.Conflict(new {message="User is already assigned to the institution."});}
        }).RequireRateLimiting("auth");
        api.MapPost("/institutions/{tenantId:guid}/users/{userId:guid}/deactivate",async (
            Guid tenantId,Guid userId,PlatformDeactivate request,ClaimsPrincipal user,
            PlatformControlStore store,CancellationToken ct)=>
        {
            if(!Guid.TryParse(user.FindFirst("sub")?.Value,out var operatorId))return Results.Forbid();
            if(tenantId!=request.TenantId)return Results.BadRequest(new {message="Tenant mismatch."});
            try{return await store.Deactivate(operatorId,tenantId,userId,request.Reason,ct)
                ?Results.NoContent():Results.NotFound();}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
            catch(InvalidOperationException ex){return Results.Conflict(new {message=ex.Message});}
        });
        return app;
    }
}
