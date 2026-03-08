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
        // 1) Sort
        var orderedItems = source.OrderBySortColumns(request.GetEffectiveSortColumns());

        // 2) Page
        var pagedItems = orderedItems
            .Skip((request.Page - 1) * request.PerPage)
            .Take(request.PerPage);

        return pagedItems;
    }

    public static async Task<PaginationResponse<TDto>> Paginate<TDto, TEntity>(this IQueryable<TEntity> source, PaginationRequest<TDto> request, IMapper<TEntity, TDto> mapper)
    {
        // 1) Sort
        var orderedItems = source.OrderBySortColumns(request.GetEffectiveSortColumns());

        // 2) Page
        var pagedItems = orderedItems
            .Skip((request.Page - 1) * request.PerPage)
            .Take(request.PerPage);

        // 3) Convert to DTO
        var dtoItems = (await Task.WhenAll(pagedItems.Select(item => mapper.Map(item)))).ToList();

        var countItems = source.Count();
        return new PaginationResponse<TDto>(request, countItems, dtoItems);
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

        var newQuery = (IOrderedQueryable<TSource>)genericMethod.Invoke(genericMethod, new object[] { query, lambda });
        return newQuery;
    }
}
