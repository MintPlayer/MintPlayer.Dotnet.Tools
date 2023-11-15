using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.Mapping.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMapper<TSource, TTarget>(this IServiceCollection services, Func<TSource, Task<TTarget>> transform)
    {
        return services.AddScoped<IMapper<TSource, TTarget>, DefaultMapper<TSource, TTarget>>((provider) =>
        {
            return ActivatorUtilities.CreateInstance<DefaultMapper<TSource, TTarget>>(provider, transform, provider);
        });
    }

    public static IServiceCollection AddMapper<TSource, TTarget>(this IServiceCollection services, Func<TSource, IServiceProvider, Task<TTarget>> transform)
    {
        return services.AddScoped<IMapper<TSource, TTarget>, DefaultMapper<TSource, TTarget>>((provider) =>
        {
            return ActivatorUtilities.CreateInstance<DefaultMapper<TSource, TTarget>>(provider, transform, provider);
        });
    }

    //public static IServiceCollection AddMapper<TSource, TTarget>(this IServiceCollection services, IMapper<TSource, TTarget> mapper)
    //{
    //    return services.AddScoped<IMapper<TSource, TTarget>>((provider) => mapper);
    //}

    public static IServiceCollection AddMapper<TMapper>(this IServiceCollection services)
    {
        var mapperType = typeof(TMapper);
        var i = typeof(TMapper).GetInterfaces();
        var ifaces = typeof(TMapper).GetInterfaces()
            .Where(iface => typeof(IMapper<,>) == iface.GetGenericTypeDefinition())
            .Select(iface => new
            {
                Interface = iface,
                TypeArguments = iface.GenericTypeArguments,
            })
            .ToList();

        if ((ifaces.Count == 0) || (ifaces.Count > 2))
        {
            throw new InvalidOperationException("AddMapper<TMapper> expects TMapper to implement IMapper<,> once, or twice with alternating type arguments");
        }

        if (ifaces.Count == 2 && ((ifaces[0].TypeArguments[0] != ifaces[1].TypeArguments[1]) || (ifaces[0].TypeArguments[1] != ifaces[1].TypeArguments[0])))
        {
            throw new InvalidOperationException("When IMapper<,> is implemented twice, AddMapper<TMapper> expects alternating type arguments");
        }

        var dm = typeof(ServiceCollectionExtensions)
            .GetMethod(nameof(AddMapper), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(TMapper), ifaces[0].TypeArguments[0], ifaces[0].TypeArguments[1]);

        // We only have to call the method once,
        // The method registers both IMappers
        var res = dm.Invoke(null, [services]);

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

    public static IMapperSource<TSource> Mapper<TSource>(this IServiceProvider services, TSource source)
    {
        return new MapperSource<TSource>(source, services);
    }
}

public interface IMapperSource<TSource>
{
    Task<TTarget> MapTo<TTarget>();
}

internal class MapperSource<TSource> : IMapperSource<TSource>
{
    private readonly TSource source;
    private readonly IServiceProvider services;
    public MapperSource(TSource source, IServiceProvider services)
    {
        this.source = source;
        this.services = services;
    }

    public async Task<TTarget> MapTo<TTarget>()
    {
        var result = await services.GetRequiredService<IMapper<TSource, TTarget>>().Map(source);
        return result;
    }
}

internal class DefaultMapper<TSource, TTarget> : IMapper<TSource, TTarget>
{
    private readonly Func<TSource, IServiceProvider, Task<TTarget>> transform;
    private readonly IServiceProvider serviceProvider;
    public DefaultMapper(Func<TSource, Task<TTarget>> transform, IServiceProvider serviceProvider)
    {
        this.transform = async (source, provider) => await transform(source);
        this.serviceProvider = serviceProvider;
    }
    public DefaultMapper(Func<TSource, IServiceProvider, Task<TTarget>> transform, IServiceProvider serviceProvider)
    {
        this.transform = transform;
        this.serviceProvider = serviceProvider;
    }

    public async Task<TTarget> Map(TSource source)
    {
        var result = await transform(source, serviceProvider);
        return result;
    }
}
