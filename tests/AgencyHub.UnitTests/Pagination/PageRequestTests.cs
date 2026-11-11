using AgencyHub.SharedKernel.Pagination;

namespace AgencyHub.UnitTests.Pagination;

public sealed class PageRequestTests
{
    [Theory]
    [InlineData(1, 20, 1, 20, 0)]
    [InlineData(3, 10, 3, 10, 20)]
    [InlineData(0, 20, 1, 20, 0)]
    [InlineData(-5, 20, 1, 20, 0)]
    [InlineData(1, 0, 1, PageRequest.DefaultPageSize, 0)]
    [InlineData(1, -1, 1, PageRequest.DefaultPageSize, 0)]
    [InlineData(1, 10_000, 1, PageRequest.MaxPageSize, 0)]
    public void Values_are_clamped_to_safe_range(int page, int pageSize, int expectedPage, int expectedPageSize, int expectedSkip)
    {
        var request = new PageRequest { Page = page, PageSize = pageSize };

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedPageSize, request.PageSize);
        Assert.Equal(expectedSkip, request.Skip);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public void TotalPages_is_rounded_up(long total, int pageSize, int expected)
    {
        var result = new PagedResult<int>([], 1, pageSize, total);

        Assert.Equal(expected, result.TotalPages);
    }
}
