namespace EduVerse.Contracts;

public sealed record RegistrationRequest(string Slug, string InstitutionName, string Kind, string ContactEmail);
public sealed record RegistrationReceipt(Guid RequestId, string Status);
public sealed record InstitutionProfile(string LegalName, string ContactEmail, string? ContactPhone,
    string? AddressLine, string? City, string? StateRegion, string? PostalCode, string OnboardingStatus);
public sealed record ProfileUpdate(string LegalName, string ContactEmail, string? ContactPhone,
    string? AddressLine, string? City, string? StateRegion, string? PostalCode);
public sealed record CampusResource(Guid Id, Guid TenantId, Guid CampusId, string Code, string Name, bool IsActive);
public sealed record ResourceCreate(Guid CampusId, string Code, string Name);
public sealed record InstitutionSetting(string Key, string Value);
public sealed record SubscriptionPlan(string Code, string Name, string Description);
public sealed record SubscriptionStatus(string? PlanCode, string Status, DateTime? ExpiresUtc, IReadOnlyList<string> Features);
public sealed record InstitutionDashboard(Guid TenantId, string Name, string Kind, string OnboardingStatus,
    int Campuses, int Departments, int Buildings, int Members, SubscriptionStatus Subscription);
