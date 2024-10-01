using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    internal class AssemblyProducer : Producer
    {
        public AssemblyProducer(IEnumerable<Models.AssemblyDeclaration> declarations, string rootNamespace) : base(rootNamespace)
        {
            Declarations = declarations;
        }

        public IEnumerable<AssemblyDeclaration> Declarations { get; }

        protected override ProducedSource? ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
