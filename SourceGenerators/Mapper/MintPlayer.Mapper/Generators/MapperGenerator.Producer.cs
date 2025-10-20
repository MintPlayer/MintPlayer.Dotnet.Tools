using Microsoft.CodeAnalysis;
using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace MintPlayer.Mapper.Generators;

public sealed class MapperProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<TypeWithMappedProperties> typesToMap;
    private readonly IEnumerable<ClassDeclaration> staticClasses;
    public MapperProducer(IEnumerable<TypeWithMappedProperties> typesToMap, IEnumerable<ClassDeclaration> staticClasses, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
        this.staticClasses = staticClasses;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        return typesToMap.Where(t => t.TypeToMap.HasError)
            .Select(type => type.TypeToMap.AppliedOn switch
            {
                EAppliedOn.Class => DiagnosticRules.GenerateMapperOneParameter.Create(type.TypeToMap.Location?.ToLocation(compilation)),
                EAppliedOn.Assembly => DiagnosticRules.GenerateMapperTwoParameters.Create(type.TypeToMap.Location?.ToLocation(compilation)),
                _ => null,
            })
            .NotNull();
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine("#nullable enable");
        writer.WriteLine(Header);
        writer.WriteLine();

        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class MapperExtensions");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static TDest? ConvertProperty<TSource, TDest>(TSource? source, int? sourceState = null, int? destState = null)");
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

        // Adding lines here breaks the MapperDebugging project build, alternatingly.
        // Add line => Build breaks => Add line => Build works => Add line => Build breaks ...
        writer.WriteLine();

        foreach (var staticClass in staticClasses)
        {
            foreach (var method in staticClass.ConversionMethods)
            {
                if (method.SourceState is not null && method.DestinationState is not null)
                {
                    writer.WriteLine($"case (global::System.Type sourceType, global::System.Type destType) when sourceType == typeof({method.SourceType}) && destType == typeof({method.DestinationType}) && sourceState == {method.SourceState} && destState == {method.DestinationState}:");
                    writer.Indent++;
                    if (method.MethodParameterCount == 3)
                        writer.WriteLine($"result = {staticClass.FullyQualifiedName}.{method.MethodName}(({method.SourceType})(object)source, ({method.StateType})sourceState, ({method.StateType})destState);");
                    else
                        writer.WriteLine($"result = {staticClass.FullyQualifiedName}.{method.MethodName}(({method.SourceType})(object)source);");
                }
                else
                {
                    writer.WriteLine($"case (global::System.Type sourceType, global::System.Type destType) when sourceType == typeof({method.SourceType}) && destType == typeof({method.DestinationType}):");
                    writer.Indent++;
                    writer.WriteLine($"result = {staticClass.FullyQualifiedName}.{method.MethodName}(({method.SourceType})(object)source);");
                }

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


        foreach (var type in typesToMap.Where(t => !t.TypeToMap.HasError))
        {
            writer.WriteLine($"public static void {type.TypeToMap.PreferredDeclaredMethodName}(this {type.TypeToMap.MappingType} input, {type.TypeToMap.DeclaredType} output)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine("if ((input is { } inValue) && (output is { }))");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var (source, destination) in type.MappedProperties)
            {
                HandleProperty(writer, source, destination, EWriteType.Assignment);
            }

            writer.Indent--;
            writer.WriteLine("}");

            writer.Indent--;
            writer.WriteLine("}");

            writer.WriteLine($"public static {type.TypeToMap.DeclaredType} {type.TypeToMap.PreferredDeclaredMethodName}(this {type.TypeToMap.MappingType} input)");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine("if (input is null) return default;");
            writer.WriteLine();

            writer.WriteLine($"return new {type.TypeToMap.DeclaredType}()");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var (source, destination) in type.MappedProperties)
            {
                HandleProperty(writer, source, destination, EWriteType.Initializer);
            }

            writer.Indent--;
            writer.WriteLine("};");

            writer.Indent--;
            writer.WriteLine("}");

            if (!type.TypeToMap.AreBothDecorated)
            {
                writer.WriteLine($"public static void {type.TypeToMap.PreferredMappingMethodName}(this {type.TypeToMap.DeclaredType} input, {type.TypeToMap.MappingType} output)");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine("if ((input is { } inValue) && (output is { }))");
                writer.WriteLine("{");
                writer.Indent++;

                foreach (var (source, destination) in type.MappedProperties)
                {
                    HandleProperty(writer, destination, source, EWriteType.Assignment);
                }

                writer.Indent--;
                writer.WriteLine("}");

                writer.Indent--;
                writer.WriteLine("}");

                writer.WriteLine($"public static {type.TypeToMap.MappingType} {type.TypeToMap.PreferredMappingMethodName}(this {type.TypeToMap.DeclaredType} input)");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine("if (input is null) return default;");
                writer.WriteLine();

                writer.WriteLine($"return new {type.TypeToMap.MappingType}()");
                writer.WriteLine("{");
                writer.Indent++;

                // TODO: add properties
                foreach (var (source, destination) in type.MappedProperties)
                {
                    HandleProperty(writer, destination, source, EWriteType.Initializer);
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

    private static void HandleProperty(IndentedTextWriter writer, PropertyDeclaration source, PropertyDeclaration destination, EWriteType writeType)
    {
        var prefix = writeType switch
        {
            EWriteType.Initializer => $"{source.PropertyName} = ",
            EWriteType.Assignment => $"output.{source.PropertyName} = ",
            _ => throw new NotImplementedException(),
        };

        var suffix = writeType switch
        {
            EWriteType.Initializer => ",",
            EWriteType.Assignment => ";",
            _ => throw new NotImplementedException(),
        };

        // Handle primitive types
        if (source.IsPrimitive && destination.IsPrimitive)
        {
            if (source.PropertyType != destination.PropertyType)
                writer.WriteLine($"{prefix}ConvertProperty<{destination.PropertyType}, {source.PropertyType}>(input.{destination.PropertyName}){suffix}");
            else if (source.StateName != null && destination.StateName != null)
                writer.WriteLine($"""{prefix}ConvertProperty<{destination.PropertyType}, {source.PropertyType}>(input.{destination.PropertyName}, {source.StateName}, {destination.StateName}){suffix}""");
            else
                writer.WriteLine($"{prefix}input.{destination.PropertyName}{suffix}");
        }
        // Handle arrays
        else if (IsArrayType(source.PropertyType) && IsArrayType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
            {
                writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToArray(){suffix}");
            }
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}()).ToArray(){suffix}");
            }
        }
        // Handle List<T>
        else if (IsListType(source.PropertyType) && IsListType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
            {
                writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToList(){suffix}");
            }
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}()).ToList(){suffix}");
            }
        }
        // Handle ICollection<T>
        else if (IsCollectionType(source.PropertyType) && IsCollectionType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
            {
                writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToList(){suffix}");
            }
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}()).ToList(){suffix}");
            }
        }
        // Handle nullable reference types
        else if (IsNullableType(source.PropertyType) && IsNullableType(destination.PropertyType))
        {
            writer.WriteLine($"{prefix}input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.MapTo{source.PropertyTypeName}(){suffix}");
        }
        // Handle complex types
        else
        {
            writer.WriteLine($"{prefix}input.{destination.PropertyName}.MapTo{source.PropertyTypeName}(){suffix}");
        }
    }
}

public sealed class MapperEntrypointProducer : Producer
{
    private readonly ImmutableArray<TypeWithMappedProperties> properties;
    public MapperEntrypointProducer(ImmutableArray<TypeWithMappedProperties> properties, string rootNamespace) : base(rootNamespace, "MapperEntrypoint.g.cs")
    {
        this.properties = properties;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        var propsInBothDirections = properties
            .Select(p => new
            {
                InType = p.TypeToMap.DeclaredType,
                OutType = p.TypeToMap.MappingType,
                Method = p.TypeToMap.PreferredMappingMethodName,
            })
            .Concat(properties.Select(p => new
            {
                InType = p.TypeToMap.MappingType,
                OutType = p.TypeToMap.DeclaredType,
                Method = p.TypeToMap.PreferredDeclaredMethodName,
            }))
            .GroupBy(p => p.InType);

        writer.WriteLine("#nullable enable");
        writer.WriteLine(Header);
        writer.WriteLine();

        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public interface IMapper");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("TDest? Map<TSource, TDest>(TSource? source);");
        writer.WriteLine("void Map<TSource, TDest>(TSource? source, TDest destination);");

        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("public class Mapper : IMapper");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public TDest? Map<TSource, TDest>(TSource? source)");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("if (source is null)");
        writer.Indent++;
        writer.WriteLine("return default;");
        writer.Indent--;

        writer.WriteLine();
        writer.WriteLine("object? result;");
        writer.WriteLine();

        writer.WriteLine($"switch (source)");
        writer.WriteLine("{");
        writer.Indent++;


        foreach (var mappedClassGrouping in propsInBothDirections)
        {
            writer.WriteLine($"case {mappedClassGrouping.Key} sourceValue:");
            writer.Indent++;

            writer.WriteLine($"switch (typeof(TDest))");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var mappedClass in mappedClassGrouping.GroupBy(mc => mc.OutType, StringComparer.Ordinal).Select(g => g.First()))
            {
                // mappedClass.OutType must be unique here !!!
                writer.WriteLine($"case global::System.Type destType when destType == typeof({mappedClass.OutType}):");
                writer.Indent++;
                writer.WriteLine($"result = global::{RootNamespace}.MapperExtensions.{mappedClass.Method}(sourceValue);");
                writer.WriteLine("break;");
                writer.Indent--;
            }

            writer.WriteLine("default:");
            writer.Indent++;
            writer.WriteLine("throw new NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");
            writer.Indent--;
            writer.Indent--;
            writer.WriteLine("}");
            writer.WriteLine("break;");
            writer.Indent--;
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
        writer.WriteLine();

        writer.WriteLine("public void Map<TSource, TDest>(TSource? source, TDest destination)");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("if (source is null) return;");

        writer.WriteLine();

        writer.WriteLine($"switch (source)");
        writer.WriteLine("{");
        writer.Indent++;

        /*** Nieuw */
        foreach (var mappedClassGrouping in propsInBothDirections)
        {
            writer.WriteLine($"case {mappedClassGrouping.Key} sourceValue:");
            writer.Indent++;

            writer.WriteLine($"switch (destination)");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var mappedClass in mappedClassGrouping.GroupBy(mc => mc.OutType, StringComparer.Ordinal).Select(g => g.First()))
            {
                // mappedClass.OutType must be unique here !!!
                writer.WriteLine($"case {mappedClass.OutType} dest:");
                writer.Indent++;
                writer.WriteLine($"global::{RootNamespace}.MapperExtensions.{mappedClass.Method}(sourceValue, dest);");
                writer.WriteLine("break;");
                writer.Indent--;
            }

            writer.WriteLine("default:");
            writer.Indent++;
            writer.WriteLine("throw new NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");

            writer.Indent--;
            writer.Indent--;
            writer.WriteLine("}");
            writer.WriteLine("break;");
            writer.Indent--;
        }

        writer.WriteLine("default:");
        writer.Indent++;
        writer.WriteLine("throw new NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");

        writer.Indent--;
        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
    }
}

public enum EWriteType
{
    Initializer,
    Assignment,
}