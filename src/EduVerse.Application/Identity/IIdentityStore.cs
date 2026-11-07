using EduVerse.Domain;

namespace EduVerse.Application.Identity;

public interface IIdentityStore
{
    Task<AccountRecord?> FindAccountAsync(string normalizedEmail, CancellationToken ct);
    Task<MembershipRecord?> FindMembershipAsync(Guid userId, string tenantSlug, CancellationToken ct);
    Task<bool> IsSessionValidAsync(Guid sessionId, Guid tenantId, Guid userId, CancellationToken ct);
    Task CreateSessionAsync(Guid sessionId, Guid tenantId, Guid userId, DateTime expiresUtc, CancellationToken ct);
    Task RevokeSessionAsync(Guid sessionId, Guid userId, CancellationToken ct);
    Task RegisterFailureAsync(Guid userId, CancellationToken ct);
    Task RegisterSuccessAsync(Guid userId, CancellationToken ct);
    Task AuditAsync(Guid? tenantId, Guid? userId, string eventCode, CancellationToken ct);
    Task SaveResetTokenAsync(Guid userId, byte[] sha256, DateTime expiresUtc, CancellationToken ct);
    Task<Guid?> ConsumeResetTokenAsync(byte[] sha256, CancellationToken ct);
    Task SetPasswordAsync(Guid userId, string hash, CancellationToken ct);
    Task<bool> IsAdminAsync(TenantIdentity identity, CancellationToken ct);
    Task<bool> DeactivateMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<AccountSummary>> ListUsersAsync(Guid tenantId, CancellationToken ct);
    Task CreateUserMembershipAsync(Guid tenantId, string email, string displayName, string passwordHash, string role, CancellationToken ct);
    Task AddRolePermissionAsync(Guid tenantId, string permission, CancellationToken ct);
    Task<bool> GrantCampusAsync(Guid tenantId, string subjectId, Guid campusId, CancellationToken ct);
    Task<bool> HasPermissionAsync(TenantIdentity identity, string permission, Guid? campusId, CancellationToken ct);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password, out bool rehashNeeded);
}

public interface IAccessTokenIssuer
{
    string Issue(Guid userId, Guid tenantId, Guid sessionId, string role, DateTimeOffset expiresUtc);
}

public interface IPasswordResetMessenger
{
    Task SendAsync(string email, string token, CancellationToken ct);
}
