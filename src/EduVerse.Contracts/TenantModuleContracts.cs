namespace EduVerse.Contracts;

/// <summary>API-owned, tenant-aware module presentation metadata; never replaces endpoint authorization.</summary>
public sealed record TenantModule(string Id,string Name,string Route,string Description,
    string Feature,bool Enabled,string? UnavailableReason);
public sealed record TenantModuleDashboard(Guid TenantId,string InstitutionName,string InstitutionKind,
    string Role,string? PlanCode,IReadOnlyList<TenantModule> Modules);
