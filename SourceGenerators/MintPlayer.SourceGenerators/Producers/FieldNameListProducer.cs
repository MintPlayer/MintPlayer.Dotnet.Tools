using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
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

            foreach (var namespaceGrouping in declarations.GroupBy(d => d.Namespace))
            {
                if (namespaceGrouping.Key != null)
                {
                    writer.WriteLine($"public namespace {namespaceGrouping.Key}");
                    writer.WriteLine("{");
                    writer.Indent++;
                }

                //foreach (var classGrouping in namespaceGrouping.GroupBy(d => d.Class)) // new { d.Class.Name, d.Class.FullyQualifiedName }
                foreach (var classGrouping in (from d in namespaceGrouping group d by d.Class.FullyQualifiedName into g select new { FullyQualifiedClassName = g.Key, Class = g.First().Class?.Name, Fields = g.ToArray(), BaseType = g.First().Class.BaseType })) // new { d.Class.Name, d.Class.FullyQualifiedName }
                {
                    writer.WriteLine($"public partial class {classGrouping.Class}");
                    writer.WriteLine("{");
                    if (classGrouping.BaseType is { } baseType
                        && baseType.Constructors is { } ctors)
                    {
                        writer.Indent++;
                        var allParams = ctors.Length > 0 ?
                            ctors[0].Parameters.Select(p => new Models.FieldDeclaration { FieldType = new Models.TypeInformation { FullyQualifiedName = p.Type.FullyQualifiedName }, FieldName = p.Name })
                            : Enumerable.Empty<Models.FieldDeclaration>();


                        writer.WriteLine($"public {classGrouping.Class}({string.Join(", ", classGrouping.Fields.Concat(allParams).Select(s => $"{s.FieldType.FullyQualifiedName} {s.FieldName}"))})");
                        if (ctors.Length > 0)
                        {
                            var joined = string.Join(", ", ctors[0].Parameters.Select(p => $"{p.Type.FullyQualifiedName} {p.Name}"));
                            var paramNames = string.Join(", ", ctors[0].Parameters.Select(p => p.Name));
                            writer.WriteLine($" : base({paramNames})");
                        }

                        writer.WriteLine("{");
                        writer.Indent++;
                        foreach (var s in classGrouping.Fields)
                        {
                            writer.WriteLine($"this.{s.FieldName} = {s.FieldName};");
                        }
                        writer.Indent--;
                        writer.WriteLine("}");
                        writer.Indent--;
                    }
                    writer.WriteLine("}");
                }

                if (namespaceGrouping.Key != null)
                {
                    writer.Indent--;
                    writer.WriteLine("}");
                }
            }

            return new ProducedSource { FileName = "FieldNameList.g.cs" };
        }

        class ConstructorParameter
        {
            public string? TypeName { get; set; }
            public string? Name { get; set; }
        }
    }
}
