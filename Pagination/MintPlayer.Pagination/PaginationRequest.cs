using System.ComponentModel;
using System.Runtime.Serialization;

namespace MintPlayer.Pagination;

[DataContract]
public class PaginationRequest<TDto>
{
    [DataMember]
    /// <summary>Number of items per page.</summary>
    public int PerPage { get; set; }

    [DataMember]
    /// <summary>Current page to load.</summary>
    public int Page { get; set; }

    [DataMember]
    /// <summary>Multi-column sort specification. Applied in order: first = primary sort, subsequent = tiebreakers.</summary>
    public SortColumn[]? SortColumns { get; set; }

    [DataMember]
    /// <summary>Legacy single sort property. Use SortColumns for multi-column sorting.</summary>
    public string SortProperty { get; set; } = string.Empty;

    [DataMember]
    /// <summary>Legacy single sort direction. Use SortColumns for multi-column sorting.</summary>
    public ListSortDirection SortDirection { get; set; }

    /// <summary>Returns SortColumns if set, otherwise constructs from legacy SortProperty/SortDirection.</summary>
    public SortColumn[] GetEffectiveSortColumns()
    {
        if (SortColumns is { Length: > 0 })
            return SortColumns;

        if (!string.IsNullOrEmpty(SortProperty))
            return [new SortColumn { Property = SortProperty, Direction = SortDirection }];

        return [];
    }
}