using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    public class RegistrationsProducer : Producer
    {
        private readonly IEnumerable<ServiceRegistration> serviceRegistrations;
        public RegistrationsProducer(IEnumerable<ServiceRegistration> serviceRegistrations, string rootNamespace) : base(rootNamespace)
        {
            this.serviceRegistrations = serviceRegistrations;
        }

        protected override ProducedSource? ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteLine(Header);
            writer.WriteLine();
            writer.WriteLine($"namespace {RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddServices(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine("return services");
            writer.Indent++;
            foreach (var svc in serviceRegistrations)
            {
                if (svc is null) continue;
                switch (svc.Lifetime)
                {
                    case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton:
                        writer.WriteLine($".AddSingleton<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                        break;
                    case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped:
                        writer.WriteLine($".AddScoped<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                        break;
                    case Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient:
                        writer.WriteLine($".AddTransient<{svc.ServiceTypeName}, {svc.ImplementationTypeName}>()");
                        break;
                }
            }
            writer.Indent--;

            writer.Indent--;
            writer.WriteLine("}");

            writer.Indent--;
            writer.WriteLine("}");

            return new ProducedSource { FileName = "ServiceMethods.g.cs" };
        }
    }
}
