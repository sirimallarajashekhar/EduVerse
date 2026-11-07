using EduVerse.Domain;

namespace EduVerse.Application.Tenancy;

/// <summary>Owns tenant authorization and campus domain validation; no rules run in Razor.</summary>
public sealed class InstitutionService(IInstitutionRepository repository, ITenantContext context)
{
    private async Task<(TenantIdentity Identity, Institution Institution)> RequireMemberAsync(CancellationToken ct)
    {
        if (!context.TryGet(out var identity) || identity is null)
            throw new UnauthorizedAccessException("Tenant context missing.");

        var tenant = await repository.GetAccessibleInstitutionAsync(identity, ct);
        if (tenant is null)
            throw new UnauthorizedAccessException("Institution membership not found or inactive.");
        return (identity, tenant);
    }

    public async Task<Institution> CurrentAsync(CancellationToken ct)
        => (await RequireMemberAsync(ct)).Institution;

    public async Task<IReadOnlyList<Campus>> CampusesAsync(CancellationToken ct)
    {
        var membership = await RequireMemberAsync(ct);
        return await repository.ListCampusesAsync(membership.Identity.TenantId, ct);
    }

    public async Task<Campus> CreateCampusAsync(string code, string name, CancellationToken ct)
    {
        var membership = await RequireMemberAsync(ct);
        if (!await repository.IsTenantAdminAsync(membership.Identity, ct))
            throw new UnauthorizedAccessException("Campus write permission required.");

        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        if (code.Length is < 2 or > 30 || !code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
            throw new ArgumentException("Campus code must be 2-30 characters (A-Z, 0-9, hyphen).", nameof(code));
        if (name.Length is < 2 or > 160)
            throw new ArgumentException("Campus name must be 2-160 characters.", nameof(name));

        return await repository.CreateCampusAsync(membership.Identity.TenantId, code, name, ct);
    }
}
