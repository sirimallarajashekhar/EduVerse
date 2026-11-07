using EduVerse.Domain;

namespace EduVerse.Application.Tenancy;

/// <summary>All institution-owned operations require a trusted tenant and subject.</summary>
public interface IInstitutionRepository
{
    Task<Institution?> GetAccessibleInstitutionAsync(TenantIdentity identity, CancellationToken ct);
    Task<IReadOnlyList<Campus>> ListCampusesAsync(TenantIdentity identity, CancellationToken ct);
    Task<bool> IsTenantAdminAsync(TenantIdentity identity, CancellationToken ct);
    Task<Campus> CreateCampusAsync(Guid tenantId, string code, string name, CancellationToken ct);
}
