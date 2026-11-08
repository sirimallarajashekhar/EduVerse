namespace EduVerse.Application.Identity;

public sealed record RbacRole(string Code,string DisplayName,IReadOnlyList<string> Permissions);
public sealed record RbacUserRole(string SubjectId,string RoleCode);
public sealed record CreateRbacRole(string Code,string DisplayName);
public sealed record SetRbacPermissions(IReadOnlyList<string> Permissions);
public sealed record SetRbacAssignment(string UserId,string RoleCode);
public interface IAdvancedRbacStore
{
    Task<IReadOnlyList<RbacRole>> ListRolesAsync(Guid tenantId,CancellationToken ct);
    Task CreateRoleAsync(Guid tenantId,string code,string displayName,Guid actorId,CancellationToken ct);
    Task ReplacePermissionsAsync(Guid tenantId,string code,IReadOnlyList<string> permissions,Guid actorId,CancellationToken ct);
    Task<IReadOnlyList<RbacUserRole>> ListAssignmentsAsync(Guid tenantId,CancellationToken ct);
    Task<bool> SetAssignmentAsync(Guid tenantId,Guid userId,string code,bool assign,Guid actorId,CancellationToken ct);
}
