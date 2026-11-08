using EduVerse.Application.Tenancy;
using EduVerse.Application.Administration;
using EduVerse.Domain;

namespace EduVerse.Application.Identity;

/// <summary>Application-layer reader authorization: validated tenant, live scoped role permission and plan.</summary>
public sealed class TenantReadAuthorizer(
    ITenantContext context,IIdentityStore identities,IInstitutionAdminStore subscriptions)
{
    public async Task<TenantIdentity> RequireAsync(string permission,Guid campus,CancellationToken ct)
    {
        if(!KnownPermissions.All.Contains(permission)||campus==Guid.Empty||
           !context.TryGet(out var actor)||actor is null||
           !await identities.HasPermissionAsync(actor,permission,campus,ct))
            throw new UnauthorizedAccessException("Campus-scoped permission required.");

        // Maintain existing plan restrictions when converting an ADMIN-only read into a delegated read.
        var feature=permission switch
        {
            "academic.read" or "attendance.read" or "attendance.manage" or "exams.read" or "exams.manage" or "learning.read" => "academics",
            "students.read" or "operations.read" => "core",
            "finance.read" => "finance",
            "college.read" => "college",
            _ => null
        };
        if(feature is not null)
        {
            var subscription=await subscriptions.SubscriptionAsync(actor.TenantId,ct);
            if(!subscription.Features.Contains(feature,StringComparer.Ordinal))
                throw new UnauthorizedAccessException("Active module subscription required.");
        }
        return actor;
    }
}
