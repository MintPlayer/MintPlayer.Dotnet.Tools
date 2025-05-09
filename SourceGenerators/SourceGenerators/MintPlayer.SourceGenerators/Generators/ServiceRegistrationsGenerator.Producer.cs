﻿using MintPlayer.SourceGenerators.Extensions;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

public class RegistrationsProducer : Producer
{
    private readonly IEnumerable<ServiceRegistration> serviceRegistrations;
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
                switch (svc.Lifetime)
                {
                    case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton:
                        writer.Write($".AddSingleton<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                        break;
                    case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped:
                        writer.Write($".AddScoped<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                        break;
                    case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient:
                        writer.Write($".AddTransient<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                        break;
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
