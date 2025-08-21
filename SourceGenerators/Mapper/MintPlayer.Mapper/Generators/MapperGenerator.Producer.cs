using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.Mapper.Generators;

public sealed class MapperProducer : Producer
{
    private readonly IEnumerable<TypeToMap> typesToMap;
    public MapperProducer(IEnumerable<TypeToMap> typesToMap, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var type in typesToMap)
        {
            if (!string.IsNullOrEmpty(type.DestinationNamespace))
            {
                writer.WriteLine($"namespace {type.DestinationNamespace}");
                writer.WriteLine("{");
                writer.Indent++;
            }

            writer.WriteLine($"public sealed class {type.DeclaredTypeName}Mapper");
            writer.WriteLine("{");
            writer.Indent++;


            writer.Indent--;
            writer.WriteLine("}");

            if (!string.IsNullOrEmpty(type.DestinationNamespace))
            {
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class MapperExtensions");
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var type in typesToMap)
        {
            writer.WriteLine($"public static {type.DeclaredType} MapTo(this {type.MappingType} input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return new {type.DeclaredType}();");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static {type.MappingType} MapTo(this {type.DeclaredType} input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return new {type.MappingType}();");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static global::System.Collections.Generic.IEnumerable<{type.DeclaredType}> MapTo(this global::System.Collections.Generic.IEnumerable<{type.MappingType}> input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return input.Select(x => x.MapTo());");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static global::System.Collections.Generic.IEnumerable<{type.MappingType}> MapTo(this global::System.Collections.Generic.IEnumerable<{type.DeclaredType}> input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return input.Select(x => x.MapTo());");

            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }
}
