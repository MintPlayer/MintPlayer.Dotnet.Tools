using Microsoft.CodeAnalysis;
using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace MintPlayer.Mapper.Generators;

public sealed class MapperProducer : Producer
{
    private readonly IEnumerable<TypeWithMappedProperties> typesToMap;
    private readonly IEnumerable<ConversionMethod> conversionMethods;
    private readonly bool hasNotNullIfNotNull;

    /// <summary>
    /// Appended to a call of a generated create overload whose result must not be null. Without
    /// <c>[NotNullIfNotNull]</c> in the compilation, such a call returns <c>D?</c> even for a non-null input.
    /// </summary>
    private readonly string notNullSuffix;

    public MapperProducer(IEnumerable<TypeWithMappedProperties> typesToMap, IEnumerable<ConversionMethod> conversionMethods, bool hasNotNullIfNotNull, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
        this.conversionMethods = conversionMethods;
        this.hasNotNullIfNotNull = hasNotNullIfNotNull;
        notNullSuffix = hasNotNullIfNotNull ? string.Empty : "!";
    }

    protected override bool WritePreamble => true;

    /// <summary>
    /// Whether the copy overload <c>Map(this S input, D output)</c> exists: <paramref name="properties"/> are the
    /// (written, read) pairs. A value-type output is a copy, so writing into it would change nothing, and an output
    /// whose mapped properties are all read-only or init-only has nothing to copy.
    /// </summary>
    internal static bool HasCopyOverload(bool outputIsValueType, IEnumerable<(PropertyDeclaration Written, PropertyDeclaration Read)> properties)
        => !outputIsValueType && properties.Any(p => !p.Written.IsReadOnly && !p.Written.IsInitOnly);

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        // Generated code must not depend on the consumer ImplicitUsings setting. Without
        // this, the emitted .Select(...) calls resolved only because a default SDK project
        // implicitly imports System.Linq; a project with ImplicitUsings disabled got CS1061.
        writer.WriteLine("using System.Linq;");
        writer.WriteLine();

        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            writer.WriteLine("/// <summary>Generated extension methods that map between the type pairs declared with <c>[GenerateMapper]</c>.</summary>");
            using (writer.OpenBlock("public static class MapperExtensions"))
            {
                writer.WriteLine("/// <summary>Converts a value with the registered <c>[MapperConversion]</c> method for <typeparamref name=\"TSource\"/> to <typeparamref name=\"TDest\"/>.</summary>");
                writer.WriteLine("/// <typeparam name=\"TSource\">The type of the value to convert.</typeparam>");
                writer.WriteLine("/// <typeparam name=\"TDest\">The type to convert to.</typeparam>");
                writer.WriteLine("/// <param name=\"source\">The value to convert.</param>");
                writer.WriteLine("/// <param name=\"sourceState\">The state of the source value, for state-aware conversion methods.</param>");
                writer.WriteLine("/// <param name=\"destState\">The state of the destination value, for state-aware conversion methods.</param>");
                writer.WriteLine("/// <returns>The converted value, or <see langword=\"default\"/> when <paramref name=\"source\"/> is <see langword=\"null\"/>.</returns>");
                writer.WriteLine("/// <exception cref=\"global::System.NotSupportedException\">No conversion method is registered for this pair of types.</exception>");
                using (writer.OpenBlock("public static TDest? ConvertProperty<TSource, TDest>(TSource? source, int? sourceState = null, int? destState = null)"))
                {
                    using (writer.OpenBlock("if (source is null)", false))
                        writer.WriteLine("return default;");

                    if (!conversionMethods.Any())
                    {
                        // A switch with only a throwing default made the return after it unreachable (CS0162).
                        writer.WriteLine("throw new global::System.NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");
                    }
                    else
                    {
                        writer.WriteLine("object? result;");
                        using (writer.OpenBlock($"switch ((typeof(TSource), typeof(TDest)))"))
                        {
                            foreach (var method in conversionMethods)
                            {
                                if (method.SourceState is not null && method.DestinationState is not null)
                                {
                                    using (writer.OpenBlock($"case (global::System.Type sourceType, global::System.Type destType) when sourceType == typeof({method.SourceType}) && destType == typeof({method.DestinationType}) && sourceState == {method.SourceState} && destState == {method.DestinationState}:", false))
                                    {
                                        if (method.MethodParameterCount == 3)
                                            writer.WriteLine($"result = {method.ContainingType}.{method.MethodName}(({method.SourceType})(object)source, ({method.StateType})sourceState, ({method.StateType})destState);");
                                        else
                                            writer.WriteLine($"result = {method.ContainingType}.{method.MethodName}(({method.SourceType})(object)source);");
                                        writer.WriteLine("break;");
                                    }
                                }
                                else
                                {
                                    using (writer.OpenBlock($"case (global::System.Type sourceType, global::System.Type destType) when sourceType == typeof({method.SourceType}) && destType == typeof({method.DestinationType}):", false))
                                    {
                                        writer.WriteLine($"result = {method.ContainingType}.{method.MethodName}(({method.SourceType})(object)source);");
                                        writer.WriteLine("break;");
                                    }
                                }
                            }

                            using (writer.OpenBlock("default:"))
                                writer.WriteLine("throw new global::System.NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");
                        }

                        writer.WriteLine("return (TDest?)result;");
                    }
                }

                foreach (var type in typesToMap)
                {
                    var t = type.TypeToMap;
                    var accessModifier = Math.Min(t.DeclaredTypeAccessibility, t.MappingTypeAccessibility) < 6 ? "internal" : "public";

                    // Mapping type -> declared type: the pairs are (declared property, mapping property).
                    WriteDirection(writer, accessModifier, t.PreferredDeclaredMethodName,
                        t.MappingType, t.MappingTypeIsValueType,
                        t.DeclaredType, t.DeclaredTypeIsValueType, t.DeclaredTypeHasParameterlessConstructor, t.DeclaredTypePrimaryConstructorParameters,
                        type.MappedProperties);

                    if (!t.AreBothDecorated)
                    {
                        WriteDirection(writer, accessModifier, t.PreferredMappingMethodName,
                            t.DeclaredType, t.DeclaredTypeIsValueType,
                            t.MappingType, t.MappingTypeIsValueType, t.MappingTypeHasParameterlessConstructor, t.MappingTypePrimaryConstructorParameters,
                            type.MappedProperties.Select(p => (p.Destination, p.Source)).ToArray());
                    }

                    WriteSequence(writer, accessModifier, t.PreferredDeclaredMethodName, t.MappingType, t.MappingTypeIsValueType, t.DeclaredType, t.DeclaredTypeIsValueType);

                    if (!t.AreBothDecorated)
                        WriteSequence(writer, accessModifier, t.PreferredMappingMethodName, t.DeclaredType, t.DeclaredTypeIsValueType, t.MappingType, t.MappingTypeIsValueType);
                }
            }
        }
    }

    /// <summary>
    /// The copy overload (when there is something to copy into a reference type) and the create overload from
    /// <paramref name="inputType"/> to <paramref name="outputType"/>. <paramref name="properties"/> are the
    /// (written output property, read input property) pairs.
    /// </summary>
    private void WriteDirection(
        IndentedTextWriter writer,
        string accessModifier,
        string methodName,
        string inputType,
        bool inputIsValueType,
        string outputType,
        bool outputIsValueType,
        bool outputHasParameterlessConstructor,
        ConstructorParameterInfo[] outputPrimaryConstructorParameters,
        (PropertyDeclaration Source, PropertyDeclaration Destination)[] properties)
    {
        var inputDoc = DocType(inputType);
        var outputDoc = DocType(outputType);

        // A reference-type input may be null; a value-type one keeps its exact type, because S? would be Nullable<S>.
        var input = inputIsValueType ? inputType : $"{inputType}?";

        if (HasCopyOverload(outputIsValueType, properties))
        {
            WriteCopyDoc(writer, inputDoc, outputDoc);
            using (writer.OpenBlock($"{accessModifier} static void {methodName}(this {input} input, {outputType} output)"))
            {
                using (writer.OpenBlock("if ((input is { } inValue) && (output is { }))"))
                {
                    foreach (var (source, destination) in properties)
                    {
                        HandleProperty(writer, source, destination, EWriteType.Assignment);
                    }
                }
            }
        }

        // Null in, null out, and the compiler knows it: [NotNullIfNotNull] makes a call with a non-null input
        // non-null, so neither the generated code nor its callers get a nullable warning.
        var returnsNull = !inputIsValueType && !outputIsValueType;
        WriteCreateDoc(writer, inputDoc, outputDoc);
        if (returnsNull && hasNotNullIfNotNull)
            writer.WriteLine("[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(\"input\")]");
        using (writer.OpenBlock($"{accessModifier} static {outputType}{(returnsNull ? "?" : string.Empty)} {methodName}(this {input} input)"))
        {
            if (!inputIsValueType)
            {
                writer.WriteLine(returnsNull ? "if (input is null) return null;" : "if (input is null) return default;");
                writer.WriteLine();
            }

            WriteNewInstance(
                writer,
                outputType,
                outputHasParameterlessConstructor,
                outputPrimaryConstructorParameters,
                properties);
        }
    }

    private void WriteSequence(IndentedTextWriter writer, string accessModifier, string methodName, string inputType, bool inputIsValueType, string outputType, bool outputIsValueType)
    {
        var suffix = !inputIsValueType && !outputIsValueType ? notNullSuffix : string.Empty;
        WriteSequenceDoc(writer, DocType(inputType), DocType(outputType));
        using (writer.OpenBlock($"{accessModifier} static global::System.Collections.Generic.IEnumerable<{outputType}> {methodName}(this global::System.Collections.Generic.IEnumerable<{inputType}> input)"))
            writer.WriteLine($"return input.Select(x => x.{methodName}(){suffix});");
    }

    // XML doc comments for the emitted mapping methods. Type names go in <c> rather than a cref:
    // a generic or nested name would need cref syntax ({T}) that the plain type string doesn't have.
    private static string DocType(string type)
        => type.Replace("global::", string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void WriteCopyDoc(IndentedTextWriter writer, string fromType, string toType)
    {
        writer.WriteLine($"/// <summary>Copies the mapped properties of a <c>{fromType}</c> onto an existing <c>{toType}</c>.</summary>");
        writer.WriteLine("/// <param name=\"input\">The object to read from.</param>");
        writer.WriteLine("/// <param name=\"output\">The object to write to.</param>");
    }

    private static void WriteCreateDoc(IndentedTextWriter writer, string fromType, string toType)
    {
        writer.WriteLine($"/// <summary>Maps a <c>{fromType}</c> to a new <c>{toType}</c>.</summary>");
        writer.WriteLine("/// <param name=\"input\">The object to map.</param>");
        writer.WriteLine("/// <returns>The new object, or <see langword=\"default\"/> when <paramref name=\"input\"/> is <see langword=\"null\"/>.</returns>");
    }

    private static void WriteSequenceDoc(IndentedTextWriter writer, string fromType, string toType)
    {
        writer.WriteLine($"/// <summary>Maps each <c>{fromType}</c> in a sequence to a new <c>{toType}</c>.</summary>");
        writer.WriteLine("/// <param name=\"input\">The objects to map.</param>");
        writer.WriteLine("/// <returns>The mapped objects, in the same order.</returns>");
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

    private string GetPropertyValueExpression(PropertyDeclaration source, PropertyDeclaration destination)
    {
        // Handle primitive types
        if (source.IsPrimitive && destination.IsPrimitive)
        {
            if (source.PropertyType != destination.PropertyType)
                return $"ConvertProperty<{destination.PropertyType}, {source.PropertyType}>(input.{destination.PropertyName})!";
            else if (source.StateName is not null && destination.StateName is not null)
                return $"ConvertProperty<{destination.PropertyType}, {source.PropertyType}>(input.{destination.PropertyName}, {source.StateName}, {destination.StateName})!";
            else
                return $"input.{destination.PropertyName}";
        }
        // Handle arrays
        else if (IsArrayType(source.PropertyType) && IsArrayType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
                return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToArray()";
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}(){notNullSuffix}).ToArray()";
            }
        }
        // Handle List<T>
        else if (IsListType(source.PropertyType) && IsListType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
                return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToList()";
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}(){notNullSuffix}).ToList()";
            }
        }
        // Handle ICollection<T>
        else if (IsCollectionType(source.PropertyType) && IsCollectionType(destination.PropertyType))
        {
            var elementType = GetElementType(source.PropertyType);
            if (IsPrimitiveOrString(elementType))
                return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.ToList()";
            else
            {
                var shortName = elementType.WithoutGlobal().Split('.').Last();
                return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.Select(x => x.MapTo{shortName}(){notNullSuffix}).ToList()";
            }
        }
        // Handle nullable reference types
        else if (IsNullableType(source.PropertyType) && IsNullableType(destination.PropertyType))
        {
            return $"input.{destination.PropertyName} == null ? null : input.{destination.PropertyName}.MapTo{source.PropertyTypeName}()";
        }
        // Handle complex types
        else
        {
            return $"input.{destination.PropertyName}.MapTo{source.PropertyTypeName}(){notNullSuffix}";
        }
    }

    private void HandleProperty(IndentedTextWriter writer, PropertyDeclaration source, PropertyDeclaration destination, EWriteType writeType)
    {
        if (source.IsReadOnly) return;
        if (source.IsInitOnly && writeType == EWriteType.Assignment) return;

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

        writer.WriteLine($"{prefix}{GetPropertyValueExpression(source, destination)}{suffix}");
    }

    /// <summary>
    /// Writes a return statement that creates a new instance of the target type,
    /// using constructor arguments when the type has no parameterless constructor.
    /// </summary>
    private void WriteNewInstance(
        IndentedTextWriter writer,
        string targetType,
        bool hasParameterlessConstructor,
        ConstructorParameterInfo[] primaryCtorParams,
        IEnumerable<(PropertyDeclaration Source, PropertyDeclaration Destination)> mappedProperties)
    {
        if (hasParameterlessConstructor || primaryCtorParams.Length == 0)
        {
            // Use object initializer (existing behavior)
            writer.WriteLine($"return new {targetType}()");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var (source, destination) in mappedProperties)
            {
                HandleProperty(writer, source, destination, EWriteType.Initializer);
            }

            writer.Indent--;
            writer.WriteLine("};");
        }
        else
        {
            // Use constructor with named arguments + optional object initializer for remaining properties
            var ctorProperties = new List<(PropertyDeclaration Source, PropertyDeclaration Destination, ConstructorParameterInfo CtorParam)>();
            var initializerProperties = new List<(PropertyDeclaration Source, PropertyDeclaration Destination)>();

            foreach (var (source, destination) in mappedProperties)
            {
                var ctorParam = primaryCtorParams.FirstOrDefault(p => p.CorrespondingPropertyName == source.PropertyName);
                if (ctorParam is not null)
                {
                    ctorProperties.Add((source, destination, ctorParam));
                }
                else if (!source.IsReadOnly)
                {
                    initializerProperties.Add((source, destination));
                }
            }

            // Write constructor call
            writer.Write($"return new {targetType}(");
            if (ctorProperties.Count > 0)
            {
                writer.WriteLine();
                writer.Indent++;
                for (int i = 0; i < ctorProperties.Count; i++)
                {
                    var (source, destination, ctorParam) = ctorProperties[i];
                    var valueExpr = GetPropertyValueExpression(source, destination);
                    var separator = i < ctorProperties.Count - 1 ? "," : "";
                    writer.WriteLine($"{ctorParam.ParameterName}: {valueExpr}{separator}");
                }
                writer.Indent--;
            }
            writer.Write(")");

            // Write object initializer for remaining properties
            var settableInitProps = initializerProperties
                .Where(p => !p.Source.IsReadOnly && !(p.Source.IsInitOnly))
                .ToList();

            if (settableInitProps.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var (source, destination) in settableInitProps)
                {
                    HandleProperty(writer, source, destination, EWriteType.Initializer);
                }
                writer.Indent--;
                writer.Write("}");
            }

            writer.WriteLine(";");
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

    private const string NotSupported = "throw new global::System.NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");";

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        var directions = properties
            .Select(p => new
            {
                InType = p.TypeToMap.DeclaredType,
                InDepth = p.TypeToMap.DeclaredTypeDepth,
                OutType = p.TypeToMap.MappingType,
                OutDepth = p.TypeToMap.MappingTypeDepth,
                Method = p.TypeToMap.PreferredMappingMethodName,
                HasCopyOverload = MapperProducer.HasCopyOverload(p.TypeToMap.MappingTypeIsValueType, p.MappedProperties.Select(m => (m.Destination, m.Source))),
            })
            .Concat(properties.Select(p => new
            {
                InType = p.TypeToMap.MappingType,
                InDepth = p.TypeToMap.MappingTypeDepth,
                OutType = p.TypeToMap.DeclaredType,
                OutDepth = p.TypeToMap.DeclaredTypeDepth,
                Method = p.TypeToMap.PreferredDeclaredMethodName,
                HasCopyOverload = MapperProducer.HasCopyOverload(p.TypeToMap.DeclaredTypeIsValueType, p.MappedProperties),
            }))
            .ToArray();

        // A type pattern for a base type before one for a derived type makes the derived case unreachable
        // (CS8120), so the switches test the deepest types first. OrderBy is stable: equal depths keep their order.
        var propsInBothDirections = directions
            .GroupBy(p => p.InType)
            .OrderByDescending(g => g.First().InDepth)
            .ToArray();

        // The copy dispatch only targets the copy overloads that exist.
        var copyDirections = directions
            .Where(p => p.HasCopyOverload)
            .GroupBy(p => p.InType)
            .OrderByDescending(g => g.First().InDepth)
            .ToArray();

        // Generated code must not depend on the consumer ImplicitUsings setting. Without
        // this, the emitted .Select(...) calls resolved only because a default SDK project
        // implicitly imports System.Linq; a project with ImplicitUsings disabled got CS1061.
        writer.WriteLine("using System.Linq;");
        writer.WriteLine();

        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            writer.WriteLine("/// <summary>Maps objects between the type pairs declared with <c>[GenerateMapper]</c>.</summary>");
            using (writer.OpenBlock("public interface IMapper"))
            {
                writer.WriteLine("/// <summary>Maps a <typeparamref name=\"TSource\"/> to a new <typeparamref name=\"TDest\"/>.</summary>");
                writer.WriteLine("/// <typeparam name=\"TSource\">The type of the object to map.</typeparam>");
                writer.WriteLine("/// <typeparam name=\"TDest\">The type to map to.</typeparam>");
                writer.WriteLine("/// <param name=\"source\">The object to map.</param>");
                writer.WriteLine("/// <returns>The new object, or <see langword=\"default\"/> when <paramref name=\"source\"/> is <see langword=\"null\"/>.</returns>");
                writer.WriteLine("/// <exception cref=\"global::System.NotSupportedException\">No mapping exists from the type of <paramref name=\"source\"/> to <typeparamref name=\"TDest\"/>.</exception>");
                writer.WriteLine("TDest? Map<TSource, TDest>(TSource? source);");
                writer.WriteLine("/// <summary>Copies the mapped properties of a <typeparamref name=\"TSource\"/> onto an existing <typeparamref name=\"TDest\"/>.</summary>");
                writer.WriteLine("/// <typeparam name=\"TSource\">The type of the object to read from.</typeparam>");
                writer.WriteLine("/// <typeparam name=\"TDest\">The type of the object to write to.</typeparam>");
                writer.WriteLine("/// <param name=\"source\">The object to read from. Nothing happens when it is <see langword=\"null\"/>.</param>");
                writer.WriteLine("/// <param name=\"destination\">The object to write to.</param>");
                writer.WriteLine("/// <exception cref=\"global::System.NotSupportedException\">No mapping exists between the types of <paramref name=\"source\"/> and <paramref name=\"destination\"/>.</exception>");
                writer.WriteLine("void Map<TSource, TDest>(TSource? source, TDest destination);");
            }

            writer.WriteLine("/// <summary>The generated <see cref=\"IMapper\"/>, dispatching to the <c>MapperExtensions</c> methods.</summary>");
            using (writer.OpenBlock("public class Mapper : IMapper"))
            {
                writer.WriteLine("/// <inheritdoc/>");
                using (writer.OpenBlock("public TDest? Map<TSource, TDest>(TSource? source)"))
                {
                    using (writer.OpenBlock("if (source is null)", false))
                        writer.WriteLine("return default;");

                    writer.WriteLine();
                    if (propsInBothDirections.Length == 0)
                    {
                        // A switch with only a throwing default made the return after it unreachable (CS0162).
                        writer.WriteLine(NotSupported);
                    }
                    else
                    {
                        writer.WriteLine("object? result;");
                        using (writer.OpenBlock($"switch (source)"))
                        {
                            foreach (var mappedClassGrouping in propsInBothDirections)
                            {
                                using (writer.OpenBlock($"case {mappedClassGrouping.Key} sourceValue:", false))
                                {
                                    using (writer.OpenBlock($"switch (typeof(TDest))"))
                                    {
                                        foreach (var mappedClass in mappedClassGrouping.GroupBy(mc => mc.OutType, StringComparer.Ordinal).Select(g => g.First()))
                                        {
                                            // mappedClass.OutType must be unique here !!!
                                            using (writer.OpenBlock($"case global::System.Type destType when destType == typeof({mappedClass.OutType}):", false))
                                            {
                                                writer.WriteLine($"result = global::{RootNamespace}.MapperExtensions.{mappedClass.Method}(sourceValue);");
                                                writer.WriteLine("break;");
                                            }
                                        }

                                        using (writer.OpenBlock("default:", false))
                                            writer.WriteLine(NotSupported);
                                    }

                                    writer.WriteLine("break;");
                                }
                            }

                            using (writer.OpenBlock("default:", false))
                                writer.WriteLine(NotSupported);
                        }

                        writer.WriteLine();
                        writer.WriteLine("return (TDest?)result;");
                    }
                }

                writer.WriteLine("/// <inheritdoc/>");
                using (writer.OpenBlock("public void Map<TSource, TDest>(TSource? source, TDest destination)"))
                {
                    writer.WriteLine("if (source is null) return;");
                    using (writer.OpenBlock($"switch (source)"))
                    {
                        foreach (var mappedClassGrouping in copyDirections)
                        {
                            using (writer.OpenBlock($"case {mappedClassGrouping.Key} sourceValue:", false))
                            {
                                using (writer.OpenBlock($"switch (destination)"))
                                {
                                    foreach (var mappedClass in mappedClassGrouping.GroupBy(mc => mc.OutType, StringComparer.Ordinal).Select(g => g.First()).OrderByDescending(mc => mc.OutDepth))
                                    {
                                        // mappedClass.OutType must be unique here !!!
                                        using (writer.OpenBlock($"case {mappedClass.OutType} dest:", false))
                                        {
                                            writer.WriteLine($"global::{RootNamespace}.MapperExtensions.{mappedClass.Method}(sourceValue, dest);");
                                            writer.WriteLine("break;");
                                        }
                                    }

                                    using (writer.OpenBlock("default:", false))
                                        writer.WriteLine("throw new global::System.NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");
                                }

                                writer.WriteLine("break;");
                            }
                        }

                        using (writer.OpenBlock("default:", false))
                            writer.WriteLine("throw new global::System.NotSupportedException($\"Conversion from {typeof(TSource)} to {typeof(TDest)} is not supported.\");");
                    }
                }
            }
        }
    }
}

public enum EWriteType
{
    Initializer,
    Assignment,
}