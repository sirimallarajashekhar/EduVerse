namespace EduVerse.Domain;

public sealed record Institution(Guid Id, string Slug, string Name, string Kind, bool IsActive);
public sealed record Campus(Guid Id, Guid TenantId, string Code, string Name, bool IsActive);
public sealed record TenantIdentity(Guid TenantId, string SubjectId);
