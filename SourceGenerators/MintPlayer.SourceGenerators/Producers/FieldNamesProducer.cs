using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    public class FieldNamesProducer : Producer
    {
        public FieldNamesProducer(string rootNamespace) : base(rootNamespace)
        {
        }

        protected override ProducedSource? ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteLine(Header);
            writer.WriteLine();

            writer.WriteLine($"namespace {RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;


            writer.Indent--;
            writer.WriteLine("}");

            return new ProducedSource { FileName = "FieldNames.g.cs" };
        }
    }
}
