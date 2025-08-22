using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.Mapper.Generators;

public sealed class MapperProducer : Producer
{
    private readonly IEnumerable<TypeWithMappedProperties> typesToMap;
    public MapperProducer(IEnumerable<TypeWithMappedProperties> typesToMap, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        //foreach (var type in typesToMap)
        //{
        //    if (!string.IsNullOrEmpty(type.DestinationNamespace))
        //    {
        //        writer.WriteLine($"namespace {type.DestinationNamespace}");
        //        writer.WriteLine("{");
        //        writer.Indent++;
        //    }

        //    writer.WriteLine($"public sealed class {type.DeclaredTypeName}Mapper");
        //    writer.WriteLine("{");
        //    writer.Indent++;


        //    writer.Indent--;
        //    writer.WriteLine("}");

        //    if (!string.IsNullOrEmpty(type.DestinationNamespace))
        //    {
        //        writer.Indent--;
        //        writer.WriteLine("}");
        //    }
        //}

        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class MapperExtensions");
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var type in typesToMap)
        {
            writer.WriteLine($"public static {type.TypeToMap.DeclaredType} MapTo{type.TypeToMap.DeclaredTypeName}(this {type.TypeToMap.MappingType} input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return new {type.TypeToMap.DeclaredType}()");
            writer.WriteLine("{");
            writer.Indent++;

            // TODO: add properties
            foreach (var (source, destination) in type.MappedProperties)
            {
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName},");
            }

            writer.Indent--;
            writer.WriteLine("};");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static {type.TypeToMap.MappingType} MapTo{type.TypeToMap.MappingTypeName}(this {type.TypeToMap.DeclaredType} input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return new {type.TypeToMap.MappingType}()");
            writer.WriteLine("{");
            writer.Indent++;

            // TODO: add properties
            foreach (var (source, destination) in type.MappedProperties)
            {
                writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName},");
            }

            writer.Indent--;
            writer.WriteLine("};");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static global::System.Collections.Generic.IEnumerable<{type.TypeToMap.DeclaredType}> MapTo{type.TypeToMap.DeclaredTypeName}(this global::System.Collections.Generic.IEnumerable<{type.TypeToMap.MappingType}> input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return input.Select(x => x.MapTo{type.TypeToMap.DeclaredTypeName}());");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static global::System.Collections.Generic.IEnumerable<{type.TypeToMap.MappingType}> MapTo{type.TypeToMap.MappingTypeName}(this global::System.Collections.Generic.IEnumerable<{type.TypeToMap.DeclaredType}> input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return input.Select(x => x.MapTo{type.TypeToMap.MappingTypeName}());");

            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }
}
