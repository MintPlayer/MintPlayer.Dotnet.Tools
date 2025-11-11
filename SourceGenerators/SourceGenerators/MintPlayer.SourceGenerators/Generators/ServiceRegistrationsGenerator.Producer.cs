using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Extensions;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

public class RegistrationsProducer : Producer
{
    private readonly IEnumerable<ServiceRegistration> serviceRegistrations;
    private readonly bool knowsDependencyInjectionAbstractions;
    private readonly Dictionary<ServiceLifetime, string> lifetimeNames = new()
    {
        { ServiceLifetime.Singleton, nameof(ServiceLifetime.Singleton) },
        { ServiceLifetime.Scoped, nameof(ServiceLifetime.Scoped) },
        { ServiceLifetime.Transient, nameof(ServiceLifetime.Transient) },
    };

    public RegistrationsProducer(IEnumerable<ServiceRegistration> serviceRegistrations, bool knowsDependencyInjectionAbstractions, string rootNamespace) : base(rootNamespace, "ServiceMethods.g.cs")
    {
        this.serviceRegistrations = serviceRegistrations;
        this.knowsDependencyInjectionAbstractions = knowsDependencyInjectionAbstractions;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine(Header);
        writer.WriteLine();

        if (knowsDependencyInjectionAbstractions)
        {
            writer.WriteLine($"using Microsoft.Extensions.DependencyInjection;");
            writer.WriteLine();
            using (writer.OpenBlock($"namespace {RootNamespace}"))
            {
                using (writer.OpenBlock("public static class DependencyInjectionExtensionMethods"))
                {
                    foreach (var methodGroup in serviceRegistrations.Where(sr => sr is not null).GroupBy(sr => sr.MethodNameHint))
                    {
                        var methodName = methodGroup.Key.NullIfEmpty() ?? "Services";
                        methodName = methodName.StartsWith("Add") ? methodName : $"Add{methodName}";
                        var accessibility = methodGroup.Select(m => m.Accessibility).Where(a => a is not EGeneratedAccessibility.Unspecified).ToArray() is { Length: > 0 } items
                            ? items.First() : EGeneratedAccessibility.Public;
                        var accessibilityString = accessibility switch
                        {
                            EGeneratedAccessibility.Internal => "internal",
                            _ => "public",
                        };
                        using (writer.OpenBlock($"{accessibilityString} static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)"))
                        {
                            writer.WriteLine("return services");
                            writer.Indent++;

                            var currentIndex = 0;
                            var total = methodGroup.Count();
                            foreach (var svc in methodGroup)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (svc.ServiceTypeName is null)
                                {
                                    if (svc.FactoryNames.Length > 0)
                                    {
                                        var currentFactoryIndex = 0;
                                        foreach (var factoryName in svc.FactoryNames)
                                        {
                                            writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}>({svc.ImplementationTypeName}.{factoryName})");
                                            if (++currentFactoryIndex != svc.FactoryNames.Length)
                                                writer.WriteLine();
                                        }
                                    }
                                    else
                                    {
                                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ImplementationTypeName}>()");
                                    }
                                }
                                else
                                {
                                    if (svc.FactoryNames.Length > 0)
                                    {
                                        var currentFactoryIndex = 0;
                                        foreach (var factoryName in svc.FactoryNames)
                                        {
                                            writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}>({svc.ImplementationTypeName}.{factoryName})");
                                            if (++currentFactoryIndex != svc.FactoryNames.Length)
                                                writer.WriteLine();
                                        }
                                    }
                                    else
                                    {
                                        writer.Write($".Add{lifetimeNames[svc.Lifetime]}<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                                    }
                                }

                                if (++currentIndex == total)
                                    writer.Write(";");

                                writer.WriteLine();
                            }

                            writer.Indent--;
                        }
                    }
                }
            }
        }
        else
        {
            writer.WriteLine("// Cannot generate service registration methods because the project does not reference Microsoft.Extensions.DependencyInjection.Abstractions");
        }
    }
}
