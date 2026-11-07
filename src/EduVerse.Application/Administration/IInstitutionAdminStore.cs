using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Administration;

public interface IInstitutionAdminStore
{
    Task<Guid> SubmitRegistrationAsync(RegistrationRequest request, CancellationToken ct);
    Task<bool> IsTenantAdminAsync(TenantIdentity identity, CancellationToken ct);
    Task<InstitutionProfile?> ProfileAsync(Guid tenantId, CancellationToken ct);
    Task SaveProfileAsync(Guid tenantId, ProfileUpdate profile, CancellationToken ct);
    Task MarkReadyAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CampusResource>> ResourcesAsync(Guid tenantId, Guid? campusId, string type, CancellationToken ct);
    Task<CampusResource?> CreateResourceAsync(Guid tenantId, ResourceCreate request, string type, CancellationToken ct);
    Task<IReadOnlyList<InstitutionSetting>> SettingsAsync(Guid tenantId, CancellationToken ct);
    Task SaveSettingAsync(Guid tenantId, string key, string value, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionPlan>> PlansAsync(CancellationToken ct);
    Task<SubscriptionStatus> SubscriptionAsync(Guid tenantId, CancellationToken ct);
    Task<InstitutionDashboard> DashboardAsync(Guid tenantId, CancellationToken ct);
}

public sealed class InvalidCampusScopeException : Exception
{
    public InvalidCampusScopeException() : base("Campus does not belong to the current institution.") { }
}
