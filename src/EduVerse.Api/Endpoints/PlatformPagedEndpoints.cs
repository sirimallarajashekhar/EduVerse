using EduVerse.Api.Modules;
using EduVerse.Application.Common;
using EduVerse.Contracts;

namespace EduVerse.Api.Endpoints;

public static class PlatformPagedEndpoints
{
    private static readonly IReadOnlySet<string> AllowedSorts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "-id", "name", "-name" };

    public static IEndpointRouteBuilder MapPlatformPagedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/modules/paged", (int? page, int? pageSize, string? sort, string? search) =>
        {
            var request = new PageQuery(page ?? 1, pageSize ?? 20, sort, search);
            if (!Pagination.TryNormalize(request, AllowedSorts, out var normalized, out var error))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = [error!] });
            IEnumerable<ModuleRegistration> items = ModuleRegistry.All;
            if (normalized.Search is { Length: > 0 } searchTerm)
                items = items.Where(x => x.DisplayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            items = normalized.Sort?.ToLowerInvariant() switch
            {
                "id" => items.OrderBy(x => x.Id, StringComparer.Ordinal),
                "-id" => items.OrderByDescending(x => x.Id, StringComparer.Ordinal),
                "-name" => items.OrderByDescending(x => x.DisplayName, StringComparer.Ordinal),
                _ => items.OrderBy(x => x.DisplayName, StringComparer.Ordinal)
            };
            return Results.Ok(Pagination.Slice(items.ToArray(), normalized));
        }).WithName("ListPlatformModulesPaged");
        return app;
    }
}
