using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    public class ClassNamesProducer : Producer
    {
        private readonly IEnumerable<Models.ClassDeclaration> declarations;
        public ClassNamesProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace)
        {
            this.declarations = declarations;
        }

        protected override ProducedSource ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteLine(Header);
            writer.WriteLine();
            writer.WriteLine($"namespace {RootNamespace};");

            writer.WriteLine("public static class ClassNames");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var declaration in declarations)
            {
                writer.WriteLine($"public const string {declaration.Name} = \"{declaration.Name}\";");
            }

            writer.Indent--;
            writer.WriteLine("}");
            return new ProducedSource { FileName = "ClassNames.g.cs" };
        }
    }
}
