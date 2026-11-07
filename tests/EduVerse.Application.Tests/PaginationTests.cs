using EduVerse.Application.Common;
using EduVerse.Contracts;
using Xunit;

namespace EduVerse.Application.Tests;

public sealed class PaginationTests
{
    private static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "-name" };

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void RejectsInvalidPageBounds(int page, int size)
    {
        Assert.False(Pagination.TryNormalize(new PageQuery(page, size), Allowed, out _, out _));
    }

    [Fact]
    public void RejectsUnknownSortAndOversizedSearch()
    {
        Assert.False(Pagination.TryNormalize(new PageQuery(Sort: "DROP TABLE"), Allowed, out _, out _));
        Assert.False(Pagination.TryNormalize(new PageQuery(Search: new string('a', 121)), Allowed, out _, out _));
    }

    [Fact]
    public void AcceptsAllowedSortAndReturnsStableEnvelope()
    {
        Assert.True(Pagination.TryNormalize(new PageQuery(2, 2, "-name", " A "), Allowed, out var request, out _));
        Assert.Equal("A", request.Search);
        var page = Pagination.Slice(new[] { 1, 2, 3, 4, 5 }, request);
        Assert.Equal(new[] { 3, 4 }, page.Items);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNext);
    }
}
