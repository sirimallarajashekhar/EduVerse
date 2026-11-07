using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Academic;

public interface IAcademicStore
{
    Task<bool> IsActiveAdminWithAcademicFeatureAsync(TenantIdentity identity, CancellationToken ct);
    Task<IReadOnlyList<AcademicYear>> YearsAsync(Guid tenantId, Guid campusId, CancellationToken ct);
    Task<AcademicYear?> CreateYearAsync(Guid tenantId, AcademicYearCreate request, CancellationToken ct);
    Task<IReadOnlyList<AcademicItem>> ItemsAsync(Guid tenantId, Guid campusId, string? kind, CancellationToken ct);
    Task<AcademicItem?> CreateItemAsync(Guid tenantId, AcademicItemCreate request, CancellationToken ct);
    Task<IReadOnlyList<FacultyAssignment>> FacultyAsync(Guid tenantId, Guid campusId, CancellationToken ct);
    Task<FacultyAssignment?> AssignFacultyAsync(Guid tenantId, FacultyAssignmentCreate request, CancellationToken ct);
    Task<IReadOnlyList<AcademicEvent>> EventsAsync(Guid tenantId, Guid campusId, CancellationToken ct);
    Task<AcademicEvent?> CreateEventAsync(Guid tenantId, AcademicEventCreate request, CancellationToken ct);
    Task<AcademicOverview> OverviewAsync(Guid tenantId, Guid campusId, CancellationToken ct);
}

public sealed class AcademicScopeException : Exception
{
    public AcademicScopeException() : base("Academic reference is outside the active campus or has an invalid relationship.") {}
}
