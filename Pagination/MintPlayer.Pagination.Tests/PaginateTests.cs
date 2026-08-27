using MintPlayer.Mapping;
using MintPlayer.Pagination;
using MintPlayer.Pagination.Extensions;

namespace MintPlayer.Pagination.Tests;

public class PaginateTests
{
    private static IQueryable<PagedPerson> People(int count = 10)
        => Enumerable.Range(1, count)
            .Select(i => new PagedPerson { Id = i, Name = $"P{i:00}", Age = 20 + i })
            .AsQueryable();

    private sealed class PersonMapper : IMapper<PagedPerson, PagedPersonDto>
    {
        public Task<PagedPersonDto> Map(PagedPerson source)
            => Task.FromResult(new PagedPersonDto { Id = source.Id, Display = source.Name });
    }

    #region Paging

    [Fact]
    public void Paginate_ReturnsTheRequestedPage()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = 2,
            PerPage = 3,
            SortProperty = nameof(PagedPerson.Id),
        };

        People().Paginate(request).Select(p => p.Id).Should().Equal([4, 5, 6]);
    }

    [Fact]
    public void Paginate_OnTheFirstPage_StartsAtTheBeginning()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = 1,
            PerPage = 4,
            SortProperty = nameof(PagedPerson.Id),
        };

        People().Paginate(request).Select(p => p.Id).Should().Equal([1, 2, 3, 4]);
    }

    [Fact]
    public void Paginate_OnAPartialLastPage_ReturnsWhatIsLeft()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = 4,
            PerPage = 3,
            SortProperty = nameof(PagedPerson.Id),
        };

        People().Paginate(request).Select(p => p.Id).Should().Equal([10]);
    }

    [Fact]
    public void Paginate_PastTheEnd_ReturnsEmpty()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = 99,
            PerPage = 3,
            SortProperty = nameof(PagedPerson.Id),
        };

        People().Paginate(request).Should().BeEmpty();
    }

    [Fact]
    public void Paginate_AppliesTheRequestedSortDirection()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = 1,
            PerPage = 3,
            SortProperty = nameof(PagedPerson.Id),
            SortDirection = System.ComponentModel.ListSortDirection.Descending,
        };

        People().Paginate(request).Select(p => p.Id).Should().Equal([10, 9, 8]);
    }

    #endregion

    #region Regressions for D2

    /// <summary>
    /// Regression for D2 in docs/PRD-TestCoverage.md. A request with neither SortColumns
    /// nor SortProperty produced an empty column array, which was handed straight to
    /// OrderBySortColumns — and that throws on an empty array. So Paginate threw
    /// ArgumentException for every unsorted request, including the default-constructed one.
    /// </summary>
    [Fact]
    public void Paginate_WithoutAnySort_DoesNotThrow()
    {
        var request = new PaginationRequest<PagedPerson> { Page = 1, PerPage = 3 };

        var result = People().Paginate(request).ToList();

        result.Should().HaveCount(3);
    }

    /// <summary>
    /// Regression for D2. Page is 1-based, so a default-constructed request has Page = 0,
    /// which produced Skip(-PerPage) and an ArgumentOutOfRangeException from LINQ.
    /// </summary>
    [Fact]
    public void Paginate_WithPageZero_IsTreatedAsTheFirstPage()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = 0,
            PerPage = 3,
            SortProperty = nameof(PagedPerson.Id),
        };

        People().Paginate(request).Select(p => p.Id).Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void Paginate_WithANegativePage_IsTreatedAsTheFirstPage()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            Page = -5,
            PerPage = 2,
            SortProperty = nameof(PagedPerson.Id),
        };

        People().Paginate(request).Select(p => p.Id).Should().Equal([1, 2]);
    }

    [Fact]
    public void Paginate_WithoutAPageSize_ReturnsEverything()
    {
        // PerPage = 0 means "no page size". Returning everything beats silently returning
        // nothing via Take(0).
        var request = new PaginationRequest<PagedPerson> { Page = 1, PerPage = 0 };

        People().Paginate(request).Should().HaveCount(10);
    }

    [Fact]
    public void Paginate_OnACompletelyDefaultRequest_ReturnsEverything()
        => People().Paginate(new PaginationRequest<PagedPerson>()).Should().HaveCount(10);

    #endregion

    #region Paginate with a mapper

    [Fact]
    public async Task Paginate_WithAMapper_ProjectsThePage()
    {
        var request = new PaginationRequest<PagedPersonDto>
        {
            Page = 2,
            PerPage = 3,
            SortProperty = nameof(PagedPerson.Id),
        };

        var response = await People().Paginate(request, new PersonMapper());

        response.Data.Select(d => d.Id).Should().Equal([4, 5, 6]);
        response.Data.Select(d => d.Display).Should().Equal(["P04", "P05", "P06"]);
    }

    [Fact]
    public async Task Paginate_WithAMapper_ReportsTheUnpagedTotal()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 3 };

        var response = await People(10).Paginate(request, new PersonMapper());

        response.TotalRecords.Should().Be(10);
        response.Data.Should().HaveCount(3);
        response.Page.Should().Be(1);
        response.PerPage.Should().Be(3);
        response.TotalPages.Should().Be(4);
    }

    [Fact]
    public async Task Paginate_WithAMapper_OnAnEmptySource_ReturnsAnEmptyPage()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 3 };

        var response = await People(0).Paginate(request, new PersonMapper());

        response.Data.Should().BeEmpty();
        response.TotalRecords.Should().Be(0);
    }

    [Fact]
    public async Task Paginate_WithAMapper_AndNoSort_DoesNotThrow()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 3 };

        var response = await People().Paginate(request, new PersonMapper());

        response.Data.Should().HaveCount(3);
    }

    #endregion

    #region PaginationResponse

    [Fact]
    public void TotalPages_RoundsUp()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 3 };

        new PaginationResponse<PagedPersonDto>(request, 10, []).TotalPages.Should().Be(4);
        new PaginationResponse<PagedPersonDto>(request, 9, []).TotalPages.Should().Be(3);
        new PaginationResponse<PagedPersonDto>(request, 1, []).TotalPages.Should().Be(1);
    }

    /// <summary>
    /// Regression for D2. TotalPages was an unguarded (totalRecords - 1) / perPage + 1,
    /// so a request with no page size threw DivideByZeroException on a property getter —
    /// including during serialization, which is where it would actually have been hit.
    /// </summary>
    [Fact]
    public void TotalPages_WithoutAPageSize_IsZeroRatherThanAThrow()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 0 };

        new PaginationResponse<PagedPersonDto>(request, 10, []).TotalPages.Should().Be(0);
    }

    /// <summary>
    /// Characterization, not an endorsement: with a page size and no records the formula
    /// yields one page rather than zero. Left as-is because changing it would alter the
    /// shape of every empty response a consumer already handles.
    /// </summary>
    [Fact]
    public void TotalPages_WithNoRecords_IsOne()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 10 };

        new PaginationResponse<PagedPersonDto>(request, 0, []).TotalPages.Should().Be(1);
    }

    [Fact]
    public void PaginationResponse_CopiesPagingFromTheRequest()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 7, PerPage = 25 };

        var response = new PaginationResponse<PagedPersonDto>(request, 500, []);

        response.Page.Should().Be(7);
        response.PerPage.Should().Be(25);
        response.TotalRecords.Should().Be(500);
        response.TotalPages.Should().Be(20);
    }

    [Fact]
    public void PaginationResponse_MaterializesTheData()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 10 };
        var enumerated = 0;

        IEnumerable<PagedPersonDto> Source()
        {
            enumerated++;
            yield return new PagedPersonDto { Id = 1 };
        }

        var response = new PaginationResponse<PagedPersonDto>(request, 1, Source());

        // The constructor does data.ToList(), so the sequence is walked once, up front.
        enumerated.Should().Be(1);
        response.Data.Should().HaveCount(1);
    }

    [Fact]
    public void PaginationResponse_SettersAreInertAndExistOnlyForTheXmlSerializer()
    {
        var request = new PaginationRequest<PagedPersonDto> { Page = 1, PerPage = 10 };
        var response = new PaginationResponse<PagedPersonDto>(request, 5, []);

        response.Page = 99;
        response.PerPage = 99;
        response.TotalRecords = 99;
        response.TotalPages = 99;
        response.Data = [new PagedPersonDto()];

        response.Page.Should().Be(1);
        response.PerPage.Should().Be(10);
        response.TotalRecords.Should().Be(5);
        response.TotalPages.Should().Be(1);
        response.Data.Should().BeEmpty();
    }

    #endregion
}
