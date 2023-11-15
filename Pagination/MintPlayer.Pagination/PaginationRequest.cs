using System.ComponentModel;

namespace MintPlayer.Pagination;

public class PaginationRequest<TDto>
{
    /// <summary>Number of items per page.</summary>
    public int PerPage { get; set; }

    /// <summary>Current page to load.</summary>
    public int Page { get; set; }

    /// <summary>Property used for sorting.</summary>
    public string SortProperty { get; set; }

    /// <summary>Sorting direction.</summary>
    public ListSortDirection SortDirection { get; set; }
}