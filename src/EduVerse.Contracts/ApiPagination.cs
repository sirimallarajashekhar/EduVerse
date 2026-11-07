namespace EduVerse.Contracts;

/// <summary>Stable page-based collection envelope for /api/v1 endpoints.</summary>
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public long TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasNext => Page < TotalPages;
}
public sealed record PageQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null);
