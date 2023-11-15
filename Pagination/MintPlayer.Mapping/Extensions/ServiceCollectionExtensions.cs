using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.Mapping.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMapper<TSource, TTarget>(this IServiceCollection services, Func<TSource, Task<TTarget>> transform)
    {
        return services.AddScoped<IMapper<TSource, TTarget>, DefaultMapper<TSource, TTarget>>((provider) =>
        {
            return ActivatorUtilities.CreateInstance<DefaultMapper<TSource, TTarget>>(provider, transform);
        });
    }

    public static IServiceCollection AddMapper<TSource, TTarget>(this IServiceCollection services, IMapper<TSource, TTarget> mapper)
    {
        return services.AddScoped<IMapper<TSource, TTarget>>((provider) => mapper);
    }

    //public static IServiceCollection AddMapper<TMapper>(this IServiceCollection services)
    //    where TMapper : IMapper
    //{
    //    return services.AddMapper()
    //}

    public static IServiceCollection AddMapper<TMapper>(this IServiceCollection services)
    {
        var mapperType = typeof(TMapper);
        var ifaces = typeof(TMapper).GetInterfaces()
            .Where(iface => iface.BaseType == typeof(IMapper<,>))
            .Select(iface => new
            {
                Interface = iface,
                TypeArguments = iface.GenericTypeArguments,
            })
            .ToList();

        if (ifaces.Count != 2)
        {
            throw new InvalidOperationException("AddMapper<TMapper> expects TMapper to implement IMapper<,> twice, with alternating type arguments");
        }

        if ((ifaces[0].TypeArguments[0] != ifaces[1].TypeArguments[1]) || (ifaces[0].TypeArguments[1] != ifaces[1].TypeArguments[0]))
        {
            throw new InvalidOperationException("AddMapper<TMapper> expects TMapper to implement IMapper<,> twice, with alternating type arguments");
        }

        typeof(ServiceCollectionExtensions)
            .GetMethod(nameof(AddMapper), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(TMapper), ifaces[0].TypeArguments[0], ifaces[0].TypeArguments[1])
            .Invoke(null, [services]);

        return services;
    }

    private static IServiceCollection AddMapper<TMapper, TSource, TTarget>(this IServiceCollection services)
        where TMapper : class, IMapper<TSource, TTarget>, IMapper<TTarget, TSource>
    {
        return services
            .AddScoped<TMapper>()
            .AddScoped((provider) =>
            {
                var m = provider.GetRequiredService<TMapper>();
                return (IMapper<TSource, TTarget>)m;
            })
            .AddScoped((provider) =>
            {
                var m = provider.GetRequiredService<TMapper>();
                return (IMapper<TTarget, TSource>)m;
            });
    }

    public static IMapper<TSource, TTarget> GetMapper<TSource, TTarget>(this IServiceProvider services)
    {
        return services.GetRequiredService<IMapper<TSource, TTarget>>();
    }

    //public static async Task<TTarget> Map<TSource, TTarget>(this IServiceProvider services, TSource source)
    //{
    //    var result = await services.GetMapper<TSource, TTarget>().Map(source);
    //    return result;
    //}

    public static async Task<TTarget> Mapper<TSource>(this IServiceProvider services, TSource source)
    {
        var result = await services.GetMapper<TSource, TTarget>().Map(source);
        return result;
    }
}

public class MapperSource<TSource>
{
    public MapperSource(TSource source)
    {
        Source = source;
    }

    public TSource Source { get; }
}

internal class DefaultMapper<TSource, TTarget> : IMapper<TSource, TTarget>
{
    private readonly Func<TSource, Task<TTarget>> transform;
    public DefaultMapper(Func<TSource, Task<TTarget>> transform)
    {
        this.transform = transform;
    }

    public async Task<TTarget> Map(TSource source)
    {
        var result = await transform(source);
        return result;
    }
}
