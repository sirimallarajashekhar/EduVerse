using System.Security.Cryptography;
using EduVerse.Domain;

namespace EduVerse.Application.Identity;

/// <summary>API-owned authentication, account lifecycle and access decisions. UI never calls a repository.</summary>
public sealed class IdentityService(IIdentityStore store, IPasswordService passwords,
    IAccessTokenIssuer issuer, IPasswordResetMessenger messenger, TimeProvider clock)
{
    public async Task<LoginTicket?> LoginAsync(string tenantSlug, string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrEmpty(password) || email.Length > 254 || tenantSlug.Length > 80)
            return null;
        var account = await store.FindAccountAsync(email.Trim().ToUpperInvariant(), ct);
        if (account is null) return null;
        var now = clock.GetUtcNow();
        if (!account.IsActive || account.LockedUntilUtc > now.UtcDateTime)
            return null;
        var goodPassword = account.PasswordHash is not null &&
            passwords.Verify(account.PasswordHash, password, out _);
        var membership = goodPassword
            ? await store.FindMembershipAsync(account.Id, tenantSlug.Trim(), ct)
            : null;
        if (membership is null)
        {
            await store.RegisterFailureAsync(account.Id, ct);
            await store.AuditAsync(null, account.Id, "AUTH_LOGIN_FAILED", ct);
            return null;
        }
        await store.RegisterSuccessAsync(account.Id, ct);
        var expires = now.AddMinutes(20);
        var sessionId = Guid.NewGuid();
        await store.CreateSessionAsync(sessionId, membership.TenantId, account.Id, expires.UtcDateTime, ct);
        await store.AuditAsync(membership.TenantId, account.Id, "AUTH_LOGIN", ct);
        var token = issuer.Issue(account.Id, membership.TenantId, sessionId, membership.RoleCode, expires);
        return new LoginTicket(token, expires, account.Email, account.DisplayName);
    }

    public async Task LogoutAsync(Guid sessionId, Guid userId, Guid tenantId, CancellationToken ct)
    {
        await store.RevokeSessionAsync(sessionId, userId, ct);
        await store.AuditAsync(tenantId, userId, "AUTH_LOGOUT", ct);
    }

    public async Task RequestResetAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return;
        var user = await store.FindAccountAsync(email.Trim().ToUpperInvariant(), ct);
        if (user is null || !user.IsActive) return;
        // Never return the token to the browser or persist it in plaintext.
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        await store.SaveResetTokenAsync(user.Id, SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)),
            clock.GetUtcNow().AddMinutes(15).UtcDateTime, ct);
        await messenger.SendAsync(user.Email, token, ct);
        await store.AuditAsync(null, user.Id, "AUTH_RESET_REQUEST", ct);
    }

    public async Task<bool> ConfirmResetAsync(string token, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128 || !StrongPassword(newPassword))
            return false;
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        var userId = await store.ConsumeResetTokenAsync(hash, ct);
        if (userId is null) return false;
        await store.SetPasswordAsync(userId.Value, passwords.Hash(newPassword), ct);
        await store.AuditAsync(null, userId, "AUTH_PASSWORD_CHANGED", ct);
        return true;
    }

    public async Task<IReadOnlyList<AccountSummary>> UsersAsync(TenantIdentity admin, CancellationToken ct)
    {
        await RequireAdminAsync(admin, ct);
        return await store.ListUsersAsync(admin.TenantId, ct);
    }

    public async Task InviteAsync(TenantIdentity admin, string email, string displayName, string role, CancellationToken ct)
    {
        await RequireAdminAsync(admin, ct);
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !email.Contains('@') ||
            string.IsNullOrWhiteSpace(displayName) || displayName.Length > 160 ||
            role is not ("MEMBER" or "ADMIN"))
            throw new ArgumentException("Invalid user details.");
        // Random unusable initial password; new user activates through one-time reset email.
        var disabledPassword = passwords.Hash(Base64Url(RandomNumberGenerator.GetBytes(48)));
        await store.CreateUserMembershipAsync(admin.TenantId, email.Trim(), displayName.Trim(), disabledPassword, role, ct);
        await RequestResetAsync(email, ct);
        await store.AuditAsync(admin.TenantId, Guid.Parse(admin.SubjectId), "IDENTITY_INVITE", ct);
    }

    public async Task GrantPermissionAsync(TenantIdentity admin, string permission, CancellationToken ct)
    {
        await RequireAdminAsync(admin, ct);
        if (!KnownPermissions.All.Contains(permission))
            throw new ArgumentException("Unknown permission.", nameof(permission));
        await store.AddRolePermissionAsync(admin.TenantId, permission, ct);
        await store.AuditAsync(admin.TenantId, Guid.Parse(admin.SubjectId), "IDENTITY_PERMISSION_GRANTED", ct);
    }

    public async Task<bool> GrantCampusAsync(TenantIdentity admin, string subjectId, Guid campusId, CancellationToken ct)
    {
        await RequireAdminAsync(admin, ct);
        if (!Guid.TryParse(subjectId, out _) || campusId == Guid.Empty)
            return false;
        var result = await store.GrantCampusAsync(admin.TenantId, subjectId, campusId, ct);
        if (result)
            await store.AuditAsync(admin.TenantId, Guid.Parse(admin.SubjectId), "IDENTITY_CAMPUS_GRANTED", ct);
        return result;
    }

    public Task<bool> AllowedAsync(TenantIdentity identity, string permission, Guid? campusId, CancellationToken ct)
        => store.HasPermissionAsync(identity, permission, campusId, ct);

    private async Task RequireAdminAsync(TenantIdentity identity, CancellationToken ct)
    {
        if (!Guid.TryParse(identity.SubjectId, out _) || !await store.IsAdminAsync(identity, ct))
            throw new UnauthorizedAccessException("Tenant administrator access required.");
    }

    public static bool StrongPassword(string? value) =>
        value is { Length: >= 12 and <= 128 } &&
        value.Any(char.IsUpper) && value.Any(char.IsLower) &&
        value.Any(char.IsDigit) && value.Any(ch => !char.IsLetterOrDigit(ch));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
