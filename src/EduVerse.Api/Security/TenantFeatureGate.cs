using EduVerse.Application.Administration;
using EduVerse.Application.Tenancy;

namespace EduVerse.Api.Security;

/// <summary>
/// Subscription gate executed after authentication but before module handlers.
/// This is API authorization; JavaScript navigation is never trusted.
/// </summary>
public static class TenantFeatureGate
{
    public static RouteGroupBuilder RequireTenantFeature(this RouteGroupBuilder group,
        string feature, bool collegeOnly = false)
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("Feature is required.",nameof(feature));
        group.AddEndpointFilter(async (context,next) =>
        {
            var services=context.HttpContext.RequestServices;
            var tenant=services.GetRequiredService<ITenantContext>();
            var institutions=services.GetRequiredService<IInstitutionRepository>();
            var subscriptions=services.GetRequiredService<IInstitutionAdminStore>();
            if(!tenant.TryGet(out var actor) || actor is null) return Results.Forbid();

            // Always check current active membership and tenant status; never trust submitted tenant IDs.
            var institution=await institutions.GetAccessibleInstitutionAsync(actor,context.HttpContext.RequestAborted);
            if(institution is null) return Results.Forbid();
            if(collegeOnly && institution.Kind is not ("College" or "Mixed")) return Results.Forbid();

            // SubscriptionAsync returns an empty feature set when expired, cancelled or suspended.
            var status=await subscriptions.SubscriptionAsync(actor.TenantId,context.HttpContext.RequestAborted);
            if(!status.Features.Contains(feature,StringComparer.Ordinal)) return Results.Forbid();

            return await next(context);
        });
        return group;
    }
}
