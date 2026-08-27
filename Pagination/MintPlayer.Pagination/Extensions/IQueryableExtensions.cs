using MintPlayer.Mapping;
using System.Linq.Expressions;

namespace MintPlayer.Pagination.Extensions;

public static class IQueryableExtensions
{
    /// <summary>Sort ascending based on a string.</summary>
    /// <param name="query">Input queryable</param>
    /// <param name="propertyName">Name of the property to sort on.</param>
    public static IOrderedQueryable<TSource> OrderBy<TSource>(this IQueryable<TSource> query, string propertyName)
    {
        return query.SortByBase(propertyName, true, isFirst: true);
    }

    /// <summary>Sort descending based on a string.</summary>
    /// <param name="query">Input queryable</param>
    /// <param name="propertyName">Name of the property to sort on.</param>
    public static IOrderedQueryable<TSource> OrderByDescending<TSource>(this IQueryable<TSource> query, string propertyName)
    {
        return query.SortByBase(propertyName, false, isFirst: true);
    }

    /// <summary>Apply multi-column sorting based on SortColumn array.</summary>
    /// <param name="query">Input queryable</param>
    /// <param name="sortColumns">Columns to sort by, in priority order.</param>
    public static IOrderedQueryable<TSource> OrderBySortColumns<TSource>(this IQueryable<TSource> query, SortColumn[] sortColumns)
    {
        if (sortColumns.Length == 0)
            throw new ArgumentException("At least one sort column is required.", nameof(sortColumns));

        var ascending = sortColumns[0].Direction == SortDirection.Ascending;
        IOrderedQueryable<TSource> ordered = query.SortByBase(sortColumns[0].Property, ascending, isFirst: true);

        for (var i = 1; i < sortColumns.Length; i++)
        {
            var asc = sortColumns[i].Direction == SortDirection.Ascending;
            ordered = ordered.SortByBase(sortColumns[i].Property, asc, isFirst: false);
        }

        return ordered;
    }

    /// <summary>Applies sorting + paging on the queryable.</summary>
    /// <typeparam name="T">Element type</typeparam>
    /// <param name="source">DbSet or Queryable</param>
    /// <param name="request">Pagination request</param>
    public static IQueryable<T> Paginate<T>(this IQueryable<T> source, PaginationRequest<T> request)
    {
        return source.ApplySort(request).ApplyPaging(request);
    }

    public static async Task<PaginationResponse<TDto>> Paginate<TDto, TEntity>(this IQueryable<TEntity> source, PaginationRequest<TDto> request, IMapper<TEntity, TDto> mapper)
    {
        var pagedItems = source.ApplySort(request).ApplyPaging(request);

        var dtoItems = (await Task.WhenAll(pagedItems.Select(item => mapper.Map(item)))).ToList();

        var countItems = source.Count();
        return new PaginationResponse<TDto>(request, countItems, dtoItems);
    }

    /// <summary>
    /// Applies the request's effective sort columns, if it has any.
    /// </summary>
    /// <remarks>
    /// A request with no sort is legitimate — page 1 of an unordered list — but it used to
    /// be handed straight to <see cref="OrderBySortColumns"/>, which throws on an empty
    /// array. So <c>Paginate</c> threw for every request that set neither SortProperty nor
    /// SortColumns, which is the default-constructed request. Note that paging without a
    /// sort has no guaranteed order across pages; <see cref="OrderBySortColumns"/> stays
    /// strict for callers who ask for sorting explicitly.
    /// </remarks>
    private static IQueryable<T> ApplySort<T, TDto>(this IQueryable<T> source, PaginationRequest<TDto> request)
    {
        var sortColumns = request.GetEffectiveSortColumns();
        return sortColumns.Length == 0 ? source : source.OrderBySortColumns(sortColumns);
    }

    /// <summary>
    /// Applies Skip/Take for the request's page.
    /// </summary>
    /// <remarks>
    /// <see cref="PaginationRequest{TDto}.Page"/> is 1-based, so a default-constructed
    /// request (Page = 0) produced <c>Skip(-PerPage)</c> and an
    /// ArgumentOutOfRangeException. A non-positive PerPage means "no page size", which is
    /// treated as no paging rather than <c>Take(0)</c> — silently returning nothing for an
    /// unset page size is worse than returning everything.
    /// </remarks>
    private static IQueryable<T> ApplyPaging<T, TDto>(this IQueryable<T> query, PaginationRequest<TDto> request)
    {
        if (request.PerPage <= 0)
            return query;

        var page = request.Page < 1 ? 1 : request.Page;

        return query
            .Skip((page - 1) * request.PerPage)
            .Take(request.PerPage);
    }

    private static IOrderedQueryable<TSource> SortByBase<TSource>(this IQueryable<TSource> query, string propertyName, bool ascending, bool isFirst)
    {
        var entityType = typeof(TSource);
        var info = entityType.GetProperty(propertyName);
        if (info == null)
            throw new Exceptions.InvalidSortPropertyException(propertyName);

        //Create x=>x.PropName
        var parameter = Expression.Parameter(typeof(TSource), "a");
        var property = Expression.Property(parameter, propertyName);
        var lambda = Expression.Lambda(property, parameter);

        string methodName;
        if (isFirst)
            methodName = ascending ? nameof(Enumerable.OrderBy) : nameof(Enumerable.OrderByDescending);
        else
            methodName = ascending ? nameof(Enumerable.ThenBy) : nameof(Enumerable.ThenByDescending);

        var enumarableType = typeof(Queryable);
        var method = enumarableType.GetMethods()
             .Where(m => m.Name == methodName && m.IsGenericMethodDefinition)
             .Where(m =>
             {
                 var parameters = m.GetParameters().ToList();
                 return parameters.Count == 2;
             }).Single();

        var genericMethod = method.MakeGenericMethod(entityType, info.PropertyType);

        // Static method, so the target is null. It used to pass genericMethod as the
        // target, which Invoke ignores for statics — harmless, but it read as a bug.
        var newQuery = (IOrderedQueryable<TSource>)genericMethod.Invoke(null, new object[] { query, lambda })!;
        return newQuery;
    }
}
