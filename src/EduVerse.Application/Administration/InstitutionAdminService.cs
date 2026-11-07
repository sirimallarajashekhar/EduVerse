using EduVerse.Contracts;
using EduVerse.Domain;
using EduVerse.Application.Tenancy;

namespace EduVerse.Application.Administration;

/// <summary>Business validation and membership authorization stay in backend Application layer.</summary>
public sealed class InstitutionAdminService(
    IInstitutionAdminStore store, IInstitutionRepository institutions, ITenantContext context)
{
    private static readonly IReadOnlySet<string> Keys = new HashSet<string>(StringComparer.Ordinal)
    {
        "timezone", "locale", "date_format", "academic_year_label", "support_email"
    };

    public async Task<Guid> RequestRegistrationAsync(RegistrationRequest request, CancellationToken ct)
    {
        var slug = request.Slug?.Trim().ToLowerInvariant() ?? "";
        var name = request.InstitutionName?.Trim() ?? "";
        var kind = request.Kind?.Trim() ?? "";
        var email = request.ContactEmail?.Trim() ?? "";
        if (slug.Length is < 3 or > 80 || !slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ||
            name.Length is < 3 or > 160 || kind is not ("School" or "College" or "Mixed") ||
            !ValidEmail(email)) throw new ArgumentException("Invalid registration details.");
        return await store.SubmitRegistrationAsync(new RegistrationRequest(slug, name, kind, email), ct);
    }

    private async Task<TenantIdentity> MemberAsync(CancellationToken ct)
    {
        if (!context.TryGet(out var identity) || identity is null ||
            await institutions.GetAccessibleInstitutionAsync(identity, ct) is null)
            throw new UnauthorizedAccessException("Inactive or missing tenant membership.");
        return identity;
    }

    private async Task<TenantIdentity> AdminAsync(CancellationToken ct)
    {
        var identity = await MemberAsync(ct);
        if (!await store.IsTenantAdminAsync(identity, ct))
            throw new UnauthorizedAccessException("Institution administrator required.");
        return identity;
    }

    public async Task<InstitutionProfile?> ProfileAsync(CancellationToken ct)
    {
        var identity = await MemberAsync(ct);
        return await store.ProfileAsync(identity.TenantId, ct);
    }

    public async Task SaveProfileAsync(ProfileUpdate request, CancellationToken ct)
    {
        var identity = await AdminAsync(ct);
        var name = request.LegalName?.Trim() ?? "";
        var email = request.ContactEmail?.Trim() ?? "";
        if (name.Length is < 3 or > 160 || !ValidEmail(email) ||
            !OptionalLength(request.ContactPhone,24) || !OptionalLength(request.AddressLine,240) ||
            !OptionalLength(request.City,100) || !OptionalLength(request.StateRegion,100) ||
            !OptionalLength(request.PostalCode,20))
            throw new ArgumentException("Invalid institution profile.");
        await store.SaveProfileAsync(identity.TenantId, request with { LegalName=name, ContactEmail=email }, ct);
    }

    public async Task MarkReadyAsync(CancellationToken ct)
    {
        var identity = await AdminAsync(ct);
        var profile = await store.ProfileAsync(identity.TenantId, ct);
        if (profile is null || string.IsNullOrWhiteSpace(profile.LegalName) ||
            string.IsNullOrWhiteSpace(profile.ContactEmail))
            throw new ArgumentException("Complete the institution profile before marking onboarding ready.");
        await store.MarkReadyAsync(identity.TenantId, ct);
    }

    public async Task<IReadOnlyList<CampusResource>> ResourcesAsync(string type, Guid? campusId, CancellationToken ct)
    {
        RequireResourceType(type);
        var identity = await AdminAsync(ct);
        return await store.ResourcesAsync(identity.TenantId, campusId, type, ct);
    }

    public async Task<CampusResource> CreateResourceAsync(string type, ResourceCreate request, CancellationToken ct)
    {
        RequireResourceType(type);
        var identity = await AdminAsync(ct);
        var code = request.Code?.Trim().ToUpperInvariant() ?? "";
        var name = request.Name?.Trim() ?? "";
        if (request.CampusId == Guid.Empty ||
            code.Length is < 2 or > 30 ||
            !code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-') ||
            name.Length is < 2 or > 160)
            throw new ArgumentException("Invalid department or building details.");
        return await store.CreateResourceAsync(identity.TenantId,
            request with { Code=code, Name=name }, type, ct)
            ?? throw new InvalidCampusScopeException();
    }

    public async Task<IReadOnlyList<InstitutionSetting>> SettingsAsync(CancellationToken ct)
    {
        var identity = await AdminAsync(ct);
        return await store.SettingsAsync(identity.TenantId, ct);
    }

    public async Task SaveSettingAsync(InstitutionSetting setting, CancellationToken ct)
    {
        var identity = await AdminAsync(ct);
        if (!Keys.Contains(setting.Key) || setting.Value is null ||
            setting.Value.Length is < 1 or > 240)
            throw new ArgumentException("Unknown setting or invalid value.");
        await store.SaveSettingAsync(identity.TenantId, setting.Key, setting.Value.Trim(), ct);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> PlansAsync(CancellationToken ct)
    {
        await AdminAsync(ct);
        return await store.PlansAsync(ct);
    }

    public async Task<SubscriptionStatus> SubscriptionAsync(CancellationToken ct)
    {
        var identity = await AdminAsync(ct);
        return await store.SubscriptionAsync(identity.TenantId, ct);
    }

    public async Task<InstitutionDashboard> DashboardAsync(CancellationToken ct)
    {
        var identity = await AdminAsync(ct);
        return await store.DashboardAsync(identity.TenantId, ct);
    }

    private static void RequireResourceType(string type)
    {
        if (type is not ("departments" or "buildings"))
            throw new ArgumentException("Unsupported institution resource.");
    }
    private static bool ValidEmail(string value) =>
        value.Length is > 3 and <= 254 && value.Contains('@') && !value.Any(char.IsWhiteSpace);
    private static bool OptionalLength(string? value, int limit) => value is null || value.Length <= limit;
}
