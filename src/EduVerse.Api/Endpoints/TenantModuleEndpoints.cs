using EduVerse.Api.Modules;
using EduVerse.Application.Administration;
using EduVerse.Application.Tenancy;
using EduVerse.Contracts;

namespace EduVerse.Api.Endpoints;

public static class TenantModuleEndpoints
{
    public static IEndpointRouteBuilder MapTenantModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/tenant/modules",async (
            ITenantContext context,IInstitutionRepository repository,IInstitutionAdminStore admin,
            CancellationToken ct)=>
        {
            if(!context.TryGet(out var identity)||identity is null)return Results.Forbid();
            var institution=await repository.GetAccessibleInstitutionAsync(identity,ct);
            if(institution is null)return Results.Forbid();
            if(!await repository.IsTenantAdminAsync(identity,ct))return Results.Forbid();
            var subscription=await admin.SubscriptionAsync(identity.TenantId,ct);
            return Results.Ok(new TenantModuleDashboard(identity.TenantId,
                institution.Name,institution.Kind,"ADMIN",subscription.PlanCode,
                TenantModuleCatalog.ForAdmin(institution.Kind,subscription.Features)));
        }).RequireAuthorization();
        return app;
    }
}
