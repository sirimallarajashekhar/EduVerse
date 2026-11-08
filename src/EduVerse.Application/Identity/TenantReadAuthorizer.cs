using EduVerse.Application.Tenancy;
using EduVerse.Domain;

namespace EduVerse.Application.Identity;

/// <summary>Reusable application-layer campus-bound reader check; never accepts a caller-supplied tenant.</summary>
public sealed class TenantReadAuthorizer(ITenantContext context,IIdentityStore identities)
{
    public async Task<TenantIdentity> RequireAsync(string permission,Guid campus,CancellationToken ct)
    {
        if(!KnownPermissions.All.Contains(permission)||campus==Guid.Empty||
           !context.TryGet(out var actor)||actor is null||
           !await identities.HasPermissionAsync(actor,permission,campus,ct))
            throw new UnauthorizedAccessException("Campus-scoped permission required.");
        return actor;
    }
}
