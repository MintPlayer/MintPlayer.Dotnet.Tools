#if CHECK_CS0718
// Build with -p:DefineConstants=CHECK_CS0718 to confirm the PRD's `static partial class` cannot be passed to
// AddResiliencePipeline<T>(): expected CS0718 (static type as type argument).
using Microsoft.Extensions.DependencyInjection;

namespace S6;

public static partial class StaticCatalogPipeline;

internal static class StaticCheck
{
    public static void Register(IServiceCollection services) => services.AddResiliencePipeline<StaticCatalogPipeline>();
}
#endif
