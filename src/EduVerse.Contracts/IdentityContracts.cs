namespace EduVerse.Contracts;

public sealed record LoginRequest(string TenantSlug, string Email, string Password);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresUtc,
    string DisplayName, string Email, string SubjectId, Guid TenantId, string RoleCode);
public sealed record PasswordResetRequest(string Email);
public sealed record PasswordResetConfirm(string Token, string Password);
public sealed record UserCreateRequest(string Email, string DisplayName, string RoleCode);
public sealed record UserSummary(string Id, string Email, string DisplayName, string RoleCode);
public sealed record PermissionGrantRequest(string RoleCode, string PermissionCode);
public sealed record CampusGrantRequest(string UserId, Guid CampusId);
public sealed record IdentityProfile(string SubjectId, Guid TenantId, string Email, string DisplayName, string RoleCode);
