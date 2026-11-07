namespace EduVerse.Application.Identity;

public sealed record AccountRecord(Guid Id, string Email, string DisplayName, string? PasswordHash,
    bool IsActive, int FailedLogins, DateTime? LockedUntilUtc);
public sealed record MembershipRecord(Guid TenantId, string SubjectId, string RoleCode);
public sealed record LoginTicket(string AccessToken, DateTimeOffset ExpiresUtc, string Email, string DisplayName);
public sealed record AccountSummary(string Id, string Email, string DisplayName, string RoleCode);
public static class KnownPermissions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "institutions.read", "institutions.campuses.manage",
        "hrms.read", "hrms.employees.manage", "hrms.payroll.read",
        "ams.read", "ams.assets.manage", "ams.maintenance.manage"
    };
}
