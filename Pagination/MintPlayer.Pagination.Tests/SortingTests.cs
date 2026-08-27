using MintPlayer.Pagination;
using MintPlayer.Pagination.Exceptions;
using MintPlayer.Pagination.Extensions;

namespace MintPlayer.Pagination.Tests;

/// <summary>
/// List&lt;T&gt;.AsQueryable() is the whole harness — no EF Core anywhere. The expression trees
/// SortByBase builds are executed by the LINQ-to-objects provider, which is enough to prove
/// the reflection and Expression.Lambda plumbing is correct.
/// </summary>
public class SortingTests
{
    private static IQueryable<PagedPerson> People() => new List<PagedPerson>
    {
        new() { Id = 3, Name = "Carol", Age = 30 },
        new() { Id = 1, Name = "alice", Age = 25 },
        new() { Id = 2, Name = "Bob",   Age = 30 },
    }.AsQueryable();

    #region OrderBy / OrderByDescending by name

    [Fact]
    public void OrderBy_SortsAscendingOnTheNamedProperty()
        => People().OrderBy(nameof(PagedPerson.Id)).Select(p => p.Id).Should().Equal([1, 2, 3]);

    [Fact]
    public void OrderByDescending_SortsDescendingOnTheNamedProperty()
        => People().OrderByDescending(nameof(PagedPerson.Id)).Select(p => p.Id).Should().Equal([3, 2, 1]);

    [Fact]
    public void OrderBy_WorksOnAStringProperty()
        => People().OrderBy(nameof(PagedPerson.Name)).Select(p => p.Name).Should().Equal(["alice", "Bob", "Carol"]);

    [Fact]
    public void OrderBy_WorksOnADateProperty()
    {
        var source = new List<PagedPerson>
        {
            new() { Id = 1, BirthDate = new DateTime(2000, 5, 1) },
            new() { Id = 2, BirthDate = new DateTime(1990, 1, 1) },
        }.AsQueryable();

        source.OrderBy(nameof(PagedPerson.BirthDate)).Select(p => p.Id).Should().Equal([2, 1]);
    }

    [Fact]
    public void OrderBy_WithAnUnknownProperty_ThrowsInvalidSortProperty()
    {
        var act = () => People().OrderBy("DoesNotExist").ToList();

        act.Should().Throw<InvalidSortPropertyException>()
            .WithMessage("*DoesNotExist*");
    }

    [Fact]
    public void OrderBy_WithAFieldName_ThrowsInvalidSortProperty()
    {
        // SortByBase uses GetProperty, so a public field is not a valid sort target.
        var act = () => People().OrderBy(nameof(PagedPerson.NotAProperty)).ToList();

        act.Should().Throw<InvalidSortPropertyException>();
    }

    [Fact]
    public void OrderBy_IsCaseSensitiveOnThePropertyName()
    {
        var act = () => People().OrderBy("id").ToList();
        act.Should().Throw<InvalidSortPropertyException>();
    }

    #endregion

    #region OrderBySortColumns

    [Fact]
    public void OrderBySortColumns_AppliesASingleColumn()
        => People()
            .OrderBySortColumns([new SortColumn { Property = nameof(PagedPerson.Id) }])
            .Select(p => p.Id).Should().Equal([1, 2, 3]);

    [Fact]
    public void OrderBySortColumns_AppliesTheSecondColumnAsATiebreaker()
    {
        var result = People().OrderBySortColumns(
        [
            new SortColumn { Property = nameof(PagedPerson.Age), Direction = SortDirection.Descending },
            new SortColumn { Property = nameof(PagedPerson.Name), Direction = SortDirection.Ascending },
        ]);

        // Age 30 first (descending), then Bob before Carol by name.
        result.Select(p => p.Name).Should().Equal(["Bob", "Carol", "alice"]);
    }

    [Fact]
    public void OrderBySortColumns_SupportsDescendingTiebreakers()
    {
        var result = People().OrderBySortColumns(
        [
            new SortColumn { Property = nameof(PagedPerson.Age), Direction = SortDirection.Descending },
            new SortColumn { Property = nameof(PagedPerson.Name), Direction = SortDirection.Descending },
        ]);

        result.Select(p => p.Name).Should().Equal(["Carol", "Bob", "alice"]);
    }

    [Fact]
    public void OrderBySortColumns_HonoursThreeColumns()
    {
        var result = People().OrderBySortColumns(
        [
            new SortColumn { Property = nameof(PagedPerson.Age) },
            new SortColumn { Property = nameof(PagedPerson.Name) },
            new SortColumn { Property = nameof(PagedPerson.Id) },
        ]);

        result.Select(p => p.Id).Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void OrderBySortColumns_WithNoColumns_Throws()
    {
        // Deliberately strict: a caller asking for sorting must say what to sort on.
        // Paginate no longer routes an unsorted request here (see PaginateTests).
        var act = () => People().OrderBySortColumns([]);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("sortColumns");
    }

    #endregion

    #region GetEffectiveSortColumns

    [Fact]
    public void GetEffectiveSortColumns_PrefersSortColumns()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            SortColumns = [new SortColumn { Property = "Id" }],
            SortProperty = "Name",
        };

        request.GetEffectiveSortColumns().Should().ContainSingle()
            .Which.Property.Should().Be("Id");
    }

    [Fact]
    public void GetEffectiveSortColumns_FallsBackToTheLegacySortProperty()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            SortProperty = "Name",
            SortDirection = System.ComponentModel.ListSortDirection.Descending,
        };

        var columns = request.GetEffectiveSortColumns();

        columns.Should().ContainSingle();
        columns[0].Property.Should().Be("Name");
        columns[0].Direction.Should().Be(SortDirection.Descending);
    }

    [Fact]
    public void GetEffectiveSortColumns_MapsLegacyAscending()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            SortProperty = "Name",
            SortDirection = System.ComponentModel.ListSortDirection.Ascending,
        };

        request.GetEffectiveSortColumns()[0].Direction.Should().Be(SortDirection.Ascending);
    }

    [Fact]
    public void GetEffectiveSortColumns_OnAnEmptyRequest_IsEmpty()
        => new PaginationRequest<PagedPerson>().GetEffectiveSortColumns().Should().BeEmpty();

    [Fact]
    public void GetEffectiveSortColumns_IgnoresAnEmptySortColumnsArray()
    {
        var request = new PaginationRequest<PagedPerson>
        {
            SortColumns = [],
            SortProperty = "Name",
        };

        request.GetEffectiveSortColumns().Should().ContainSingle()
            .Which.Property.Should().Be("Name");
    }

    [Fact]
    public void SortColumn_DefaultsToAscending()
        => new SortColumn().Direction.Should().Be(SortDirection.Ascending);

    #endregion
}
