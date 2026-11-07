using EduVerse.Contracts;

namespace EduVerse.Application.Common;

/// <summary>Centralizes bounds and field allowlists. Never concatenate untrusted sort expressions into SQL.</summary>
public static class Pagination
{
    public const int MaxPageSize = 100;
    public static bool TryNormalize(PageQuery query, IReadOnlySet<string> allowedSorts,
        out PageQuery normalized, out string? error)
    {
        normalized = query;
        error = null;
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > MaxPageSize)
        {
            error = "Page must be >= 1 and pageSize must be between 1 and 100.";
            return false;
        }
        var search = query.Search?.Trim();
        if (search?.Length > 120)
        {
            error = "Search must be at most 120 characters.";
            return false;
        }
        var sort = string.IsNullOrWhiteSpace(query.Sort) ? null : query.Sort.Trim();
        if (sort is not null && !allowedSorts.Contains(sort))
        {
            error = "Unsupported sort field.";
            return false;
        }
        // Avoid overflow when SQL OFFSET uses 32-bit bounds.
        if ((long)(query.Page - 1) * query.PageSize > int.MaxValue)
        {
            error = "Requested page is out of range.";
            return false;
        }
        normalized = query with { Sort = sort, Search = search };
        return true;
    }
    public static PageResult<T> Slice<T>(IReadOnlyList<T> source, PageQuery query)
    {
        var offset = (query.Page - 1) * query.PageSize;
        return new PageResult<T>(source.Skip(offset).Take(query.PageSize).ToArray(),
            query.Page, query.PageSize, source.Count);
    }
}
