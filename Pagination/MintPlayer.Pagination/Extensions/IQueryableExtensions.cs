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
        return query.OrderByBase(propertyName, true);
    }

    /// <summary>Sort descending based on a string.</summary>
    /// <param name="query">Input queryable</param>
    /// <param name="propertyName">Name of the property to sort on.</param>
    public static IOrderedQueryable<TSource> OrderByDescending<TSource>(this IQueryable<TSource> query, string propertyName)
    {
        return query.OrderByBase(propertyName, false);
    }

    /// <summary>Applies sorting + paging on the queryable.</summary>
    /// <typeparam name="TDto">DTO type</typeparam>
    /// <typeparam name="TEntity">Entity type</typeparam>
    /// <param name="source">DbSet or Queryable</param>
    /// <param name="request">Pagination request</param>
    public static IQueryable<T> Paginate<T>(this IQueryable<T> source, PaginationRequest<T> request)
    {
        // 1) Sort
        var orderedItems = request.SortDirection == System.ComponentModel.ListSortDirection.Descending
            ? source.OrderByDescending(request.SortProperty)
            : source.OrderBy(request.SortProperty);

        // 2) Page
        var pagedItems = orderedItems
            .Skip((request.Page - 1) * request.PerPage)
            .Take(request.PerPage);

        return pagedItems;
    }


    public static async Task<PaginationResponse<TDto>> Paginate<TDto, TEntity>(this IQueryable<TEntity> source, PaginationRequest<TDto> request, IMapper<TEntity, TDto> mapper)
    {
        // 1) Sort
        var orderedItems = request.SortDirection == System.ComponentModel.ListSortDirection.Descending
            ? source.OrderByDescending(request.SortProperty)
            : source.OrderBy(request.SortProperty);

        // 2) Page
        var pagedItems = orderedItems
            .Skip((request.Page - 1) * request.PerPage)
            .Take(request.PerPage);

        // 3) Convert to DTO
        var dtoItems = (await Task.WhenAll(pagedItems.Select(item => mapper.Map(item)))).ToList();

        var countItems = source.Count();
        return new PaginationResponse<TDto>(request, countItems, dtoItems);
    }

    private static IOrderedQueryable<TSource> OrderByBase<TSource>(this IQueryable<TSource> query, string propertyName, bool ascending)
    {
        var entityType = typeof(TSource);
        var info = entityType.GetProperty(propertyName);
        if (info == null)
            throw new Exceptions.InvalidSortPropertyException(propertyName);

        //Create x=>x.PropName
        // The parameter for our lambda expression
        var parameter = Expression.Parameter(typeof(TSource), "a");
        // A reference to the property
        var property = Expression.Property(parameter, propertyName);
        // Our lambda expression
        var lambda = Expression.Lambda(property, parameter);

        //Get System.Linq.Queryable.OrderBy() method.
        var enumarableType = typeof(Queryable);
        var method = enumarableType.GetMethods()
             .Where(m => m.Name == (ascending ? nameof(Enumerable.OrderBy) : nameof(Enumerable.OrderByDescending)) && m.IsGenericMethodDefinition)
             .Where(m => {
                 var parameters = m.GetParameters().ToList();
                 //Put more restriction here to ensure selecting the right overload                
                 return parameters.Count == 2; //overload that has 2 parameters
             }).Single();

        //The linq's OrderBy<TSource, TKey> has two generic types, which provided here
        var genericMethod = method.MakeGenericMethod(entityType, info.PropertyType);

        /*
         * Call query.OrderBy(selector), with query and selector: x=> x.PropName
         * Note that we pass the selector as Expression to the method and we don't compile it.
         * By doing so EF can extract "order by" columns and generate SQL for it.
         */
        var newQuery = (IOrderedQueryable<TSource>)genericMethod.Invoke(genericMethod, new object[] { query, lambda });
        return newQuery;
    }
}
