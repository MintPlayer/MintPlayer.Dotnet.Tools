using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Generators;

/// <summary>
/// <c>[GenericMethod(n)]</c> on a private method that takes one collection of <c>object</c>: generates public overloads
/// <c>M&lt;T1, T2&gt;(T1 t1, T2 t2)</c> up to arity n, each forwarding its arguments as one collection expression.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class GenericMethodSourceGenerator : IncrementalGenerator
{
    /// <summary>The metadata name of <c>MintPlayer.SourceGenerators.Attributes.GenericMethodAttribute</c>.</summary>
    public const string AttributeMetadataName = "MintPlayer.SourceGenerators.Attributes.GenericMethodAttribute";

    public const int MinCount = 2;
    public const int MaxCount = 16;
    public const int DefaultCount = 4;

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var discovered = context.SyntaxProvider.ForAttribute(
            AttributeMetadataName,
            static (node, ct) => node is MethodDeclarationSyntax,
            static (ctx, ct) => Discover(ctx, ct));

        var methodsSourceProvider = discovered
            .Models()
            // The models are value-equatable ([GenerateEquality]), so the collected array matches the previous run's.
            .Collect()
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new GenericMethodProducer(providers.Item1, providers.Item2.RootNamespace!));

        context.ProduceCode(methodsSourceProvider);
        context.ReportDiagnostics(discovered.Diagnostics());
    }

    private static Discovered<Models.GenericMethodDeclaration>? Discover(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method || method.ContainingType is not { } type) return null;

        var location = method.Locations.FirstOrDefault();
        var displayName = method.Name;
        var diagnostics = new List<DiagnosticInfo>();

        var pathSpec = type.GetPathSpec(ct);
        if (pathSpec is null) return null;
        if (!pathSpec.IsSupported)
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.GenericMethodTypeNotExtendable, location, displayName, pathSpec.UnsupportedTypeName, Describe(pathSpec.UnsupportedReason)));

        // The accessibility, not the keyword: a method without a modifier is private, and `private protected` is not.
        if (method.DeclaredAccessibility != Accessibility.Private)
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.GenericMethodMustBePrivate, location, displayName, Describe(method.DeclaredAccessibility)));

        if (ShapeProblem(method, ctx.SemanticModel.Compilation, out var elementsNotNull) is { } problem)
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.GenericMethodInvalidShape, location, displayName, problem));

        // The compiler's constant, so `Count = MaxArity`, `2 + 1`, `(int)3uL` and the constructor's default all work.
        var attribute = ctx.Attributes.FirstOrDefault();
        var count = attribute.GetArgument("count", DefaultCount);
        if (count is < MinCount or > MaxCount)
        {
            var attributeLocation = attribute?.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? location;
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.GenericMethodCountOutOfRange, attributeLocation, displayName, count));
        }

        if (diagnostics.Count > 0)
            return Discovered<Models.GenericMethodDeclaration>.Fail([.. diagnostics]);

        // `T1` is the natural name; a type in scope with a `T1` gets `TArg1` (or `TArg_1`, ...) instead of a shadowing T1 (G5).
        var prefix = pathSpec.UniqueTypeParameterPrefix("T");
        if (prefix != "T") prefix = pathSpec.UniqueTypeParameterPrefix("TArg");

        return new Discovered<Models.GenericMethodDeclaration>(new Models.GenericMethodDeclaration
        {
            PathSpec = pathSpec,
            MethodName = SymbolExtensions.EscapeIdentifier(method.Name),
            IsStatic = method.IsStatic,
            ReturnType = method.ReturnsVoid ? null : method.ReturnType.ToDisplayString(ReturnTypeFormat),
            ElementsNotNull = elementsNotNull,
            TypeParameterPrefix = prefix,
            Count = count,
        });
    }

    private static readonly SymbolDisplayFormat ReturnTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Why the overloads could not forward to <paramref name="method"/> (the end of "Method 'X' ..."), or null when it
    /// takes exactly one by-value parameter that a collection expression can build from <c>object</c> elements.
    /// </summary>
    private static string? ShapeProblem(IMethodSymbol method, Compilation compilation, out bool elementsNotNull)
    {
        elementsNotNull = false;
        if (method.MethodKind != MethodKind.Ordinary) return "is not an ordinary method";
        if (method.IsGenericMethod) return "is generic, so the forwarded collection cannot infer its type arguments";
        if (method.ReturnsByRef || method.ReturnsByRefReadonly) return "returns by reference";
        if (method.Parameters.Length != 1) return $"takes {method.Parameters.Length} parameters";

        var parameter = method.Parameters[0];
        if (parameter.RefKind != RefKind.None) return $"takes its parameter by reference ({parameter.RefKind.ToString().ToLowerInvariant()})";

        var element = CollectionElementType(parameter.Type, compilation);
        if (element is null) return $"takes '{parameter.Type.ToDisplayString()}', which a collection expression cannot build";
        if (element.SpecialType != SpecialType.System_Object && element.TypeKind != TypeKind.Dynamic)
            return $"takes a collection of '{element.ToDisplayString()}', not of object";

        elementsNotNull = element.NullableAnnotation == NullableAnnotation.NotAnnotated;
        return null;
    }

    /// <summary>The element type of a collection-expression target type, or null when it is not one.</summary>
    private static ITypeSymbol? CollectionElementType(ITypeSymbol type, Compilation compilation)
    {
        switch (type)
        {
            case IArrayTypeSymbol { Rank: 1 } array:
                return array.ElementType;
            case INamedTypeSymbol { TypeArguments.Length: 1 } span
                when span.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>":
                return span.TypeArguments[0];
            case INamedTypeSymbol { TypeKind: TypeKind.Interface, TypeArguments.Length: 1 } @interface
                when @interface.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
                    or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
                    or SpecialType.System_Collections_Generic_IReadOnlyList_T
                    or SpecialType.System_Collections_Generic_ICollection_T
                    or SpecialType.System_Collections_Generic_IList_T:
                return @interface.TypeArguments[0];
            case INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false } named:
                var element = named.AllInterfaces
                    .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                    ?.TypeArguments[0];
                if (element is null) return null;

                // [CollectionBuilder] (ImmutableArray<T>, ...), or a constructible type with an Add method (List<T>, ...).
                var builder = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.CollectionBuilderAttribute");
                if (builder is not null && named.OriginalDefinition.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, builder)))
                    return element;
                var constructible = named.TypeKind == TypeKind.Struct
                    || named.InstanceConstructors.Any(c => c.Parameters.All(p => p.IsOptional || p.IsParams));
                var hasAdd = named.GetAllBaseTypes()
                    .SelectMany(t => t.GetMembers("Add"))
                    .OfType<IMethodSymbol>()
                    .Any(m => !m.IsStatic && m.Parameters.Length >= 1);
                return constructible && hasAdd ? element : null;
            default:
                return null;
        }
    }

    private static string Describe(EPathSpecUnsupportedReason reason) => reason switch
    {
        EPathSpecUnsupportedReason.FileLocal => "is file-local, so a generated partial would be another type",
        EPathSpecUnsupportedReason.UnsupportedKind => "is not a class, struct, record or interface",
        _ => "is not partial",
    };

    private static string Describe(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => accessibility.ToString().ToLowerInvariant(),
    };
}
