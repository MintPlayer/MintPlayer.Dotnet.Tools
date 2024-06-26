﻿using System.Runtime.Serialization;

namespace MintPlayer.Pagination;

[DataContract]
public class PaginationResponse<TDto>
{
    public PaginationResponse(PaginationRequest<TDto> request, int totalRecords, IEnumerable<TDto> data)
    {
        this.data = data.ToList();
        page = request.Page;
        perPage = request.PerPage;
        this.totalRecords = totalRecords;
    }

    // Setters are only there to support the XmlSerializer

    #region Data
    private List<TDto> data;
    [DataMember]
    public List<TDto> Data
    {
        get => data;
        set { }
    }
    #endregion
    #region Page
    private int page;
    /// <summary>Current page to load, readonly.</summary>
    [DataMember]
    public int Page
    {
        get => page;
        set { }
    }
    #endregion
    #region PerPage
    private int perPage;
    /// <summary>Number of items per page, readonly.</summary>
    [DataMember]
    public int PerPage
    {
        get => perPage;
        set { }
    }
    #endregion
    #region TotalRecords
    private int totalRecords;
    /// <summary>Total number of records, readonly.</summary>
    [DataMember]
    public int TotalRecords
    {
        get => totalRecords;
        set { }
    }
    #endregion
    #region TotalPages
    /// <summary>Total number of pages, readonly.</summary>
    [DataMember]
    public int TotalPages
    {
        get => (totalRecords - 1) / perPage + 1;
        set { }
    }
    #endregion
}
