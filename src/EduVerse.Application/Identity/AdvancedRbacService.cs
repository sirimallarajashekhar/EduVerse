using EduVerse.Application.Tenancy;
namespace EduVerse.Application.Identity;

/// <summary>Custom roles are additive to MEMBER; tenant ADMIN stays a separate protected membership.</summary>
public sealed class AdvancedRbacService(IAdvancedRbacStore store,IIdentityStore identities,ITenantContext context)
{
    private async Task<(Guid Tenant,Guid Actor)> RequireAdmin(CancellationToken ct)
    {
        if(!context.TryGet(out var identity) || identity is null ||
           !Guid.TryParse(identity.SubjectId,out var user) ||
           !await identities.IsAdminAsync(identity,ct))
            throw new UnauthorizedAccessException();
        return (identity.TenantId,user);
    }
    public async Task<IReadOnlyList<RbacRole>> Roles(CancellationToken ct)
    {
        var actor=await RequireAdmin(ct);
        return await store.ListRolesAsync(actor.Tenant,ct);
    }
    public async Task<IReadOnlyList<RbacUserRole>> Assignments(CancellationToken ct)
    {
        var actor=await RequireAdmin(ct);
        return await store.ListAssignmentsAsync(actor.Tenant,ct);
    }
    public async Task Create(CreateRbacRole role,CancellationToken ct)
    {
        var actor=await RequireAdmin(ct);
        if(role is null || !ValidCode(role.Code) || string.IsNullOrWhiteSpace(role.DisplayName) ||
           role.DisplayName.Trim().Length>100)
            throw new ArgumentException("Use a role code of 3-32 uppercase letters, numbers or underscores, and a display name.");
        await store.CreateRoleAsync(actor.Tenant,role.Code,role.DisplayName.Trim(),actor.Actor,ct);
    }
    public async Task SetPermissions(string code,SetRbacPermissions request,CancellationToken ct)
    {
        var actor=await RequireAdmin(ct);
        if(!ValidCode(code) || request.Permissions is null || request.Permissions.Count>50 ||
           request.Permissions.Any(x=>!KnownPermissions.All.Contains(x)))
            throw new ArgumentException("Permission list contains unsupported permissions.");
        await store.ReplacePermissionsAsync(actor.Tenant,code,request.Permissions.Distinct(StringComparer.Ordinal).ToArray(),actor.Actor,ct);
    }
    public async Task<bool> Assign(SetRbacAssignment request,bool enabled,CancellationToken ct)
    {
        var actor=await RequireAdmin(ct);
        if(!Guid.TryParse(request.UserId,out var target)||target==Guid.Empty||!ValidCode(request.RoleCode))
            throw new ArgumentException("Invalid role or subject.");
        return await store.SetAssignmentAsync(actor.Tenant,target,request.RoleCode,enabled,actor.Actor,ct);
    }
    private static bool ValidCode(string? value) =>
        value is {Length:>=3 and <=32} && value!="ADMIN" && value!="MEMBER" &&
        value!="PLATFORM_SUPER_ADMIN" && value[0] is >= 'A' and <= 'Z' &&
        value.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
}
