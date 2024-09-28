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

            foreach (var namespaceGrouping in declarations.GroupBy(d => d.Namespace))
            {
                if (namespaceGrouping.Key != null)
                {
                    source.AppendLine($"public namespace {namespaceGrouping.Key}");
                    source.AppendLine("{");
                }

                //foreach (var classGrouping in namespaceGrouping.GroupBy(d => d.Class)) // new { d.Class.Name, d.Class.FullyQualifiedName }
                foreach (var classGrouping in (from d in namespaceGrouping group d by d.Class.FullyQualifiedName into g select new { FullyQualifiedClassName = g.Key, Class = g.First().Class?.Name, Fields = g.ToArray(), BaseType = g.First().Class.BaseType })) // new { d.Class.Name, d.Class.FullyQualifiedName }
                {
                    source.AppendLine($"    public partial class {classGrouping.Class}");
                    source.AppendLine("    {");
                    if (classGrouping.BaseType is { } baseType
                        && baseType.Constructors is { } ctors)
                    {
                        var allParams = ctors.Length > 0 ?
                            ctors[0].Parameters.Select(p => new { TypeName = p.Type.FullyQualifiedName, Name = p.Name }).ToArray()
                            : new [];

                        source.AppendLine($"        public {classGrouping.Class}({string.Join(", ", classGrouping.Fields.Select(s => $"{s.FieldType.FullyQualifiedName} {s.FieldName}"))})");
                        if (ctors.Length > 0)
                        {
                            var joined = string.Join(", ", ctors[0].Parameters.Select(p => $"{p.Type.FullyQualifiedName} {p.Name}"));
                            var paramNames = string.Join(", ", ctors[0].Parameters.Select(p => p.Name));
                            source.AppendLine($"            : base({paramNames})");
                        }

                        source.AppendLine("        {");
                        foreach (var s in classGrouping.Fields)
                        {
                            source.AppendLine($"            this.{s.FieldName} = {s.FieldName};");
                        }
                        source.AppendLine("        }");
                    }
                    source.AppendLine("    }");
                }

                if (namespaceGrouping.Key != null)
                {
                    source.AppendLine("}");
                }
            }


            var sourceText = source.ToString();
            var fileName = $"FieldNameList.g.cs";

            return new ProducedSource { FileName = fileName, Source = sourceText };
        }
    }
}
