namespace EduVerse.Application.Identity;

public sealed record AccountRecord(Guid Id, string Email, string DisplayName, string? PasswordHash,
    bool IsActive, int FailedLogins, DateTime? LockedUntilUtc);
public sealed record MembershipRecord(Guid TenantId, string SubjectId, string RoleCode);
public sealed record LoginTicket(string AccessToken, DateTimeOffset ExpiresUtc,
    string Email, string DisplayName, Guid UserId, Guid TenantId, string RoleCode);
public sealed record AccountSummary(string Id, string Email, string DisplayName, string RoleCode);
public static class KnownPermissions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "institutions.read", "institutions.campuses.manage",
        "hrms.read", "hrms.employees.manage", "hrms.payroll.read",
        "ams.read", "ams.assets.manage", "ams.maintenance.manage",
        "academic.read", "academic.manage", "students.read", "students.manage",
        "attendance.read", "attendance.manage", "finance.read", "finance.manage",
        "exams.read", "exams.manage", "college.read", "college.manage",
        "operations.read", "operations.manage", "learning.read", "learning.manage",
        "reporting.read", "workflows.read", "workflows.manage"
    };
}
