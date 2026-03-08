using System.ComponentModel;
using System.Runtime.Serialization;

namespace MintPlayer.Pagination;

[DataContract]
public class SortColumn
{
    [DataMember]
    /// <summary>Property name to sort on.</summary>
    public string Property { get; set; } = string.Empty;

    [DataMember]
    /// <summary>Sort direction for this column.</summary>
    public ListSortDirection Direction { get; set; }
}
