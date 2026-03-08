using System.Runtime.Serialization;

namespace MintPlayer.Pagination;

public enum SortDirection
{
    [EnumMember(Value = "ascending")]
    Ascending,

    [EnumMember(Value = "descending")]
    Descending
}

[DataContract]
public class SortColumn
{
    [DataMember]
    /// <summary>Property name to sort on.</summary>
    public string Property { get; set; } = string.Empty;

    [DataMember]
    /// <summary>Sort direction for this column.</summary>
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}
