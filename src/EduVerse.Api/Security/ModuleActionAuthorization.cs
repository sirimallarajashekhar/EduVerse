using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using EduVerse.Application.Administration;

namespace EduVerse.Api.Security;

/// <summary>
/// A mandatory module boundary: admin writes remain protected; delegated GETs need a live
/// tenant role permission and explicit active campus. Handler services own row-level scopes.
/// </summary>
public static class ModuleActionAuthorization
{
    public static RouteGroupBuilder RequireModuleActions(this RouteGroupBuilder group,
        string readPermission, string managePermission, params string[] delegatedReadRoutes)
    {
        ArgumentException.ThrowIfNullOrEmpty(readPermission);
        ArgumentException.ThrowIfNullOrEmpty(managePermission);
        var delegated = delegatedReadRoutes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        group.AddEndpointFilter(async (ctx,next) =>
        {
            var http=ctx.HttpContext;
            var tenant=http.RequestServices.GetRequiredService<ITenantContext>();
            var identity=http.RequestServices.GetRequiredService<IIdentityStore>();
            if(!tenant.TryGet(out var actor)||actor is null)return Results.Forbid();
            if(await identity.IsAdminAsync(actor,http.RequestAborted))return await next(ctx);
            // Admin-only mutations are intentional until business services safely support delegation.
            if(http.Request.Method!="GET")return Results.Forbid();
            var route=http.Request.Path.Value;
            if(route is null||!delegated.Contains(route))return Results.Forbid();
            if(!Guid.TryParse(http.Request.Query["campusId"],out var campus)||campus==Guid.Empty)
                return Results.Forbid();
            if(!await identity.HasPermissionAsync(actor,readPermission,campus,http.RequestAborted))
                return Results.Forbid();
            return await next(ctx);
        });
        return group;
    }
}
