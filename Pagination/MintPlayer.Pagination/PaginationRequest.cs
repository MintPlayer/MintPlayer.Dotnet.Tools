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
    /// <summary>Property used for sorting.</summary>
    public string SortProperty { get; set; } = string.Empty;

    [DataMember]
    /// <summary>Sorting direction.</summary>
    public ListSortDirection SortDirection { get; set; }
}