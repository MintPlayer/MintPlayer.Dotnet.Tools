using MintPlayer.SourceGenerators.Tools;
using System;
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

        protected override ProducedSource? ProduceSource(CancellationToken cancellationToken)
        {
            var source = new StringBuilder();
            source.AppendLine(Header);
            source.AppendLine();

            foreach (var declaration in declarations.GroupBy(d => d.Namespace))
            {
                if (declaration.Key != null)
                {
                    source.AppendLine($"public namespace {declaration.Key}");
                    source.AppendLine("{");
                }

                foreach (var classDeclaration in declaration.GroupBy(d => d.ClassName))
                {
                    source.AppendLine($"    public partial class {classDeclaration.Key}");
                    source.AppendLine("    {");
                    source.AppendLine($"        public {classDeclaration.Key}({string.Join(", ", classDeclaration.Select(s => $"{s.FullyQualifiedTypeName} {s.Name}"))})");
                    source.AppendLine("        {");
                    foreach (var s in classDeclaration)
                    {
                        source.AppendLine($"            this.{s.Name} = {s.Name};");
                    }
                    source.AppendLine("        }");
                    source.AppendLine("    }");
                }

                if (declaration.Key != null)
                {
                    source.AppendLine("}");
                }
            }


            var sourceText = source.ToString();
            var fileName = $"Classes.g.cs";

            return new ProducedSource { FileName = fileName, Source = sourceText };
        }
    }
}
