using EduVerse.Domain;

namespace EduVerse.Application.Tenancy;

/// <summary>Tenant selection must come from a validated authentication principal.</summary>
public interface ITenantContext
{
    bool TryGet(out TenantIdentity? identity);
}
