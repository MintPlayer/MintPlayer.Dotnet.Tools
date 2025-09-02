using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Extensions;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

public class RegistrationsProducer : Producer
{
    private readonly IEnumerable<ServiceRegistration> serviceRegistrations;
    private readonly Dictionary<ServiceLifetime, string> lifetimeNames = new()
    {
        { ServiceLifetime.Singleton, nameof(ServiceLifetime.Singleton) },
        { ServiceLifetime.Scoped, nameof(ServiceLifetime.Scoped) },
        { ServiceLifetime.Transient, nameof(ServiceLifetime.Transient) },
    };

    public RegistrationsProducer(IEnumerable<ServiceRegistration> serviceRegistrations, string rootNamespace) : base(rootNamespace, "ServiceMethods.g.cs")
    {
        this.serviceRegistrations = serviceRegistrations;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();
        writer.WriteLine($"using Microsoft.Extensions.DependencyInjection;");
        writer.WriteLine();
        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class DependencyInjectionExtensionMethods");
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var methodGroup in serviceRegistrations.Where(sr => sr is not null).GroupBy(sr => sr.MethodNameHint))
        {
            var methodName = methodGroup.Key.NullIfEmpty() ?? "Services";
            methodName = methodName.StartsWith("Add") ? methodName : $"Add{methodName}";
            writer.WriteLine($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine("return services");
            writer.Indent++;

            var currentIndex = 0;
            var total = methodGroup.Count();
            foreach (var svc in methodGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (svc.ServiceTypeName is null)
                {
                    if (svc.FactoryName is not null)
                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ImplementationTypeName}>({svc.ImplementationTypeName}.{svc.FactoryName})");
                    else
                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ImplementationTypeName}>()");
                }
                else
                {
                    if (svc.FactoryName is not null)
                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}>({svc.ImplementationTypeName}.{svc.FactoryName})");
                    else
                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                }

                if (++currentIndex == total)
                    writer.Write(";");

                writer.WriteLine();
            }

            writer.Indent--;

            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }
}
