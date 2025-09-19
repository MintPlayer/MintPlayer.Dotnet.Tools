using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;
using System.CodeDom.Compiler;

namespace MintPlayer.Mapper.Generators;

public sealed class MapperProducer : Producer
{
    private readonly IEnumerable<TypeWithMappedProperties> typesToMap;
    private readonly IEnumerable<ClassDeclaration> staticClasses;
    public MapperProducer(IEnumerable<TypeWithMappedProperties> typesToMap, IEnumerable<ClassDeclaration> staticClasses, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
        this.staticClasses = staticClasses;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class MapperExtensions");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static TDest? ConvertProperty<TSource, TDest>(TSource? source)");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("if (source is null)");
        writer.Indent++;
        writer.WriteLine("return default;");
        writer.Indent--;

        writer.WriteLine();
        writer.WriteLine("object? result;");
        writer.WriteLine();

        writer.WriteLine($"switch ((typeof(TSource), typeof(TDest)))");
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var staticClass in staticClasses)
        {
            foreach (var method in staticClass.ConversionMethods)
            {
                writer.WriteLine($"case (global::System.Type sourceType, global::System.Type destType) when sourceType == typeof({method.SourceType}) && destType == typeof({method.DestinationType}):");
                writer.Indent++;
                writer.WriteLine($"result = {staticClass.FullyQualifiedName}.{method.MethodName}(({method.SourceType})(object)source);");
                writer.WriteLine("break;");

                writer.Indent--;
            }
        }

        writer.WriteLine("default:");
        writer.Indent++;
        writer.WriteLine("throw new NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");

        writer.Indent--;
        writer.Indent--;
        writer.WriteLine("}");

        writer.WriteLine();
        writer.WriteLine("return (TDest?)result;");

        writer.Indent--;
        writer.WriteLine("}");


        foreach (var type in typesToMap)
        {
            writer.WriteLine($"public static {type.TypeToMap.DeclaredType} {type.TypeToMap.PreferredDeclaredMethodName}(this {type.TypeToMap.MappingType} input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return new {type.TypeToMap.DeclaredType}()");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var (source, destination) in type.MappedProperties)
            {
                HandleProperty(writer, source, destination);
            }

            writer.Indent--;
            writer.WriteLine("};");

            writer.Indent--;
            writer.WriteLine("}");

            if (!type.TypeToMap.AreBothDecorated)
            {
                writer.WriteLine($"public static {type.TypeToMap.MappingType} {type.TypeToMap.PreferredMappingMethodName}(this {type.TypeToMap.DeclaredType} input)");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine($"return new {type.TypeToMap.MappingType}()");
                writer.WriteLine("{");
                writer.Indent++;

                // TODO: add properties
                foreach (var (source, destination) in type.MappedProperties)
                {
                    HandleProperty(writer, destination, source);
                }

                writer.Indent--;
                writer.WriteLine("};");

                writer.Indent--;
                writer.WriteLine("}");
            }

            writer.WriteLine($"public static global::System.Collections.Generic.IEnumerable<{type.TypeToMap.DeclaredType}> {type.TypeToMap.PreferredDeclaredMethodName}(this global::System.Collections.Generic.IEnumerable<{type.TypeToMap.MappingType}> input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine($"return input.Select(x => x.{type.TypeToMap.PreferredDeclaredMethodName}());");

            writer.Indent--;
            writer.WriteLine("}");

            if (!type.TypeToMap.AreBothDecorated)
            {
                writer.WriteLine($"public static global::System.Collections.Generic.IEnumerable<{type.TypeToMap.MappingType}> {type.TypeToMap.PreferredMappingMethodName}(this global::System.Collections.Generic.IEnumerable<{type.TypeToMap.DeclaredType}> input)");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine($"return input.Select(x => x.{type.TypeToMap.PreferredMappingMethodName}());");

                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }

    // Helper methods for type checks
    private static bool IsArrayType(string type)
        => type.EndsWith("[]");

    private static bool IsListType(string type)
        => type.StartsWith("global::System.Collections.Generic.List<") || type.StartsWith("List<");

    private static bool IsCollectionType(string type)
        => type.StartsWith("global::System.Collections.Generic.ICollection<") || type.StartsWith("ICollection<");

    private static bool IsNullableType(string type)
        => type.EndsWith("?") || type.StartsWith("System.Nullable<");

    private static bool IsPrimitiveOrString(string type)
    {
        switch (type.WithoutGlobal())
        {
            case "string":
            case "System.String":
            case "System.DateTime":
            case "System.DateTimeOffset":
            case "bool":
            case "System.Boolean":
            case "byte":
            case "System.Byte":
            case "sbyte":
            case "System.SByte":
            case "char":
            case "System.Char":
            case "decimal":
            case "System.Decimal":
            case "double":
            case "System.Double":
            case "float":
            case "System.Single":
            case "int":
            case "System.Int32":
            case "uint":
            case "System.UInt32":
            case "nint":
            case "nuint":
            case "long":
            case "System.Int64":
            case "ulong":
            case "System.UInt64":
            case "short":
            case "System.Int16":
            case "ushort":
            case "System.UInt16":
            case "System.IntPtr":
            case "System.UIntPtr":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Computes the fully-qualified name of the element type of a collection type.
    /// </summary>
    private static string GetElementType(string collectionType)
    {
        int start = collectionType.IndexOf('<');
        int end = collectionType.LastIndexOf('>');
        if (start >= 0 && end > start)
        {
            return collectionType.Substring(start + 1, end - start - 1).Trim();
        }
        if (collectionType.EndsWith("[]"))
        {
            return collectionType.Substring(0, collectionType.Length - 2);
        }
        return collectionType;
    }

    private static void HandleProperty(IndentedTextWriter writer, PropertyDeclaration source, PropertyDeclaration destination)
    {
        // Handle primitive types
        if (source.IsPrimitive && destination.IsPrimitive)
        {
            if (source.PropertyType == destination.PropertyType)
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName},");
            else
                writer.WriteLine($"{source.PropertyName} = ConvertProperty<{destination.PropertyType}, {source.PropertyType}>(input.{destination.PropertyName}),");
        }
        // Handle arrays
        else if (IsArrayType(source.PropertyType) && IsArrayType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
            {
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToArray(),");
            }
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}()).ToArray(),");
            }
        }
        // Handle List<T>
        else if (IsListType(source.PropertyType) && IsListType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
            {
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToList(),");
            }
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}()).ToList(),");
            }
        }
        // Handle ICollection<T>
        else if (IsCollectionType(source.PropertyType) && IsCollectionType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
            {
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToList(),");
            }
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}()).ToList(),");
            }
        }
        // Handle nullable reference types
        else if (IsNullableType(source.PropertyType) && IsNullableType(destination.PropertyType))
        {
            writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.MapTo{source.PropertyTypeName}(),");
        }
        // Handle complex types
        else
        {
            writer.WriteLine($"{source.PropertyName} = input.{destination.PropertyName}.MapTo{source.PropertyTypeName}(),");
        }
    }
}