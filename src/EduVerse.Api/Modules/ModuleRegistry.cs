using EduVerse.Contracts;

namespace EduVerse.Api.Modules;

/// <summary>Explicit, allowlisted module registrations; deployments configure routes separately.</summary>
public static class ModuleRegistry
{
    public static IReadOnlyList<ModuleRegistration> All { get; } = Array.AsReadOnly(new[]
    {
        new ModuleRegistration("ams", "Asset Management", "/ams", "/api/ams/v1", "Institution assets, inventory, transfers and maintenance.", "Preview"),
        new ModuleRegistration("hrms", "Human Resources", "/hrms", "/api/hrms/v1", "Employees, leave, attendance and payroll.", "Preview")
    });
}
