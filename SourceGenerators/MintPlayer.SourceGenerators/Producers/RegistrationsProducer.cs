using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
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
            writer.WriteLine($"namespace {RootNamespace};");

            //throw new NotImplementedException();
            return new ProducedSource { FileName = "d.g.cs" };
        }
    }
}
