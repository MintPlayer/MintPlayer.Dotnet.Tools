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

            foreach (var (source, destination) in type.MappedProperties)
            {
                // Handle primitive types
                if (source.IsPrimitive && destination.IsPrimitive)
                {
                    writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName},");
                }
                // Handle arrays
                else if (IsArrayType(source.PropertyType) && IsArrayType(destination.PropertyType))
                {
                    writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{source.PropertyName}.Select(x => x.MapTo{destination.PropertyTypeName}()).ToArray(),");
                }
                // Handle List<T>
                else if (IsListType(source.PropertyType) && IsListType(destination.PropertyType))
                {
                    writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{source.PropertyName}.Select(x => x.MapTo{destination.PropertyTypeName}()).ToList(),");
                }
                // Handle ICollection<T>
                else if (IsCollectionType(source.PropertyType) && IsCollectionType(destination.PropertyType))
                {
                    writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{source.PropertyName}.Select(x => x.MapTo{destination.PropertyTypeName}()).ToList(),");
                }
                // Handle nullable reference types
                else if (IsNullableType(source.PropertyType) && IsNullableType(destination.PropertyType))
                {
                    writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{source.PropertyName}.MapTo{destination.PropertyTypeName}(),");
                }
                // Handle complex types
                else
                {
                    writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName}.MapTo{destination.PropertyTypeName}(),");
                }
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
                // Handle primitive types
                if (source.IsPrimitive && destination.IsPrimitive)
                {
                    writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName},");
                }
                // Handle arrays
                else if (IsArrayType(source.PropertyType) && IsArrayType(destination.PropertyType))
                {
                    writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName} == null ? null : input.{source.PropertyName}.Select(x => x.MapTo{source.PropertyTypeName}()).ToArray(),");
                }
                // Handle List<T>
                else if (IsListType(source.PropertyType) && IsListType(destination.PropertyType))
                {
                    writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName} == null ? null : input.{source.PropertyName}.Select(x => x.MapTo{source.PropertyTypeName}()).ToList(),");
                }
                // Handle ICollection<T>
                else if (IsCollectionType(source.PropertyType) && IsCollectionType(destination.PropertyType))
                {
                    writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName} == null ? null : input.{source.PropertyName}.Select(x => x.MapTo{source.PropertyTypeName}()).ToList(),");
                }
                // Handle nullable reference types
                else if (IsNullableType(source.PropertyType) && IsNullableType(destination.PropertyType))
                {
                    writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName} == null ? null : input.{source.PropertyName}.MapTo{source.PropertyTypeName}(),");
                }
                // Handle complex types
                else
                {
                    writer.WriteLine($"{destination.PropertyName} = input.{source.PropertyName}.MapTo{source.PropertyTypeName}(),");
                }
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

    // Helper methods for type checks
    private static bool IsArrayType(string type)
    {
        return type.EndsWith("[]");
    }

    private static bool IsListType(string type)
    {
        return type.StartsWith("global::System.Collections.Generic.List<") || type.StartsWith("List<");
    }

    private static bool IsCollectionType(string type)
    {
        return type.StartsWith("global::System.Collections.Generic.ICollection<") || type.StartsWith("ICollection<");
    }

    private static bool IsNullableType(string type)
    {
        return type.EndsWith("?") || type.StartsWith("System.Nullable<");
    }
}
