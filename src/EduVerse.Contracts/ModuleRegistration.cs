namespace EduVerse.Contracts;

/// <summary>Public module discovery metadata. No authorization decisions should rely on this record.</summary>
public sealed record ModuleRegistration(
    string Id,
    string DisplayName,
    string RoutePrefix,
    string ApiPrefix,
    string Description,
    string Status);
