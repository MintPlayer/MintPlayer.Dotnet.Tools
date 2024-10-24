using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    public class FieldNameListProducer : Producer
    {
        private readonly IEnumerable<Models.FieldDeclaration> declarations;
        public FieldNameListProducer(IEnumerable<Models.FieldDeclaration> declarations, string rootNamespace) : base(rootNamespace)
        {
            this.declarations = declarations;
        }

        protected override ProducedSource? ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteLine(Header);
            writer.WriteLine();

            foreach (var declaration in declarations.GroupBy(d => d.Namespace))
            {
                if (declaration.Key != null)
                {
                    writer.WriteLine($"namespace {declaration.Key}");
                    writer.WriteLine("{");
                    writer.Indent++;
                }

                foreach (var classDeclaration in declaration.GroupBy(d => d.ClassName))
                {
                    writer.WriteLine($"public partial class {classDeclaration.Key}");
                    writer.WriteLine("{");
                    writer.Indent++;
                    writer.WriteLine($"public {classDeclaration.Key}({string.Join(", ", classDeclaration.Select(s => $"{s.FullyQualifiedTypeName} {s.Name}"))})");
                    writer.WriteLine("{");
                    writer.Indent++;
                    foreach (var s in classDeclaration)
                    {
                        writer.WriteLine($"this.{s.Name} = {s.Name};");
                    }
                    writer.Indent--;
                    writer.WriteLine("}");
                    writer.Indent--;
                    writer.WriteLine("}");
                }

                if (declaration.Key != null)
                {
                    writer.Indent--;
                    writer.WriteLine("}");
                }
            }

            return new ProducedSource { FileName = "FieldNameList.g.cs" };
        }
    }
}
