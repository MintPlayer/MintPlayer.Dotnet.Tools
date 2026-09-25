using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.Generators;

/// <summary>Turns a type symbol into a <see cref="DiscoveredType"/>: the model to generate from, and its diagnostics.</summary>
/// <remarks>
/// Everything that needs a symbol happens here, inside the pipeline's transform. What comes out is plain strings and
/// flags, so the steps after it compare by value and the producer never sees a symbol.
/// </remarks>
internal static class Discovery
{
    public const string AttributesNamespace = "MintPlayer.ValueComparerGenerator.Attributes";
    public const string AutoValueComparerMetadataName = AttributesNamespace + ".AutoValueComparerAttribute";

    public static bool IsAttribute(INamedTypeSymbol? attributeClass, string name)
        => attributeClass is not null
        && attributeClass.Name == name
        && attributeClass.ContainingNamespace?.ToDisplayString() == AttributesNamespace;

    public static bool HasAutoValueComparer(INamedTypeSymbol type)
    {
        foreach (var attribute in type.OriginalDefinition.GetAttributes())
            if (IsAttribute(attribute.AttributeClass, "AutoValueComparerAttribute"))
                return true;
        return false;
    }

    /// <summary>The furthest <c>[AutoValueComparer]</c> ancestor: the root of the hierarchy, or null when there is none.</summary>
    public static INamedTypeSymbol? FindHierarchyRoot(INamedTypeSymbol type)
    {
        INamedTypeSymbol? root = null;
        for (var b = type.BaseType; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
            if (HasAutoValueComparer(b)) root = b;
        return root;
    }

    /// <summary>A model is a type with the attribute, or one deriving from such a type: both get generated equality.</summary>
    public static bool IsModel(INamedTypeSymbol type) => HasAutoValueComparer(type) || FindHierarchyRoot(type) is not null;

    public static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        return false;
    }

    /// <summary>The syntax filter of the derived-types provider: a class or record class with a base list.</summary>
    public static bool IsDerivableDeclaration(SyntaxNode node)
        => node is ClassDeclarationSyntax { BaseList: not null }
        || node is RecordDeclarationSyntax { BaseList: not null } record && !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);

    /// <summary>
    /// True when <paramref name="node"/> is the part of <paramref name="type"/> that produces its model: the first
    /// declaration with a base list. A partial type with several such parts would otherwise produce several models.
    /// </summary>
    public static bool IsOwningDeclaration(INamedTypeSymbol type, SyntaxNode node)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is TypeDeclarationSyntax { BaseList: not null } declaration)
                return declaration == node;
        return false;
    }

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static DiscoveredType? Build(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        var hasAttribute = HasAutoValueComparer(type);
        var root = FindHierarchyRoot(type);
        if (!hasAttribute && root is null) return null;
        if (type.IsStatic || type.IsFileLocal || type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return null;

        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<DiagnosticInfo>();
        var typeLocation = type.Locations.FirstOrDefault(l => l.IsInSource);

        if (!IsPartial(type))
        {
            diagnostics.Add(root is null
                ? Diagnostic(nameof(ValueComparerDiagnostics.ModelNotPartial), typeLocation, type.Name)
                : Diagnostic(nameof(ValueComparerDiagnostics.DerivedTypeNotPartial), typeLocation, type.Name, root.Name));
            return new DiscoveredType { Diagnostics = diagnostics.ToEquatableArray() };
        }

        var parents = new List<ContainingTypeDeclaration>();
        for (var parent = type.ContainingType; parent is not null; parent = parent.ContainingType)
        {
            if (!IsPartial(parent))
            {
                diagnostics.Add(Diagnostic(nameof(ValueComparerDiagnostics.ContainingTypeNotPartial), typeLocation, type.Name, parent.Name));
                return new DiscoveredType { Diagnostics = diagnostics.ToEquatableArray() };
            }
            parents.Insert(0, new ContainingTypeDeclaration { Keyword = KeywordOf(parent), Name = DeclaredName(parent) });
        }

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var shape = type switch
        {
            { IsRecord: true, IsValueType: true } => EqualityShape.RecordStruct,
            { IsValueType: true } => EqualityShape.Struct,
            { IsRecord: true } => root is null ? EqualityShape.Record : EqualityShape.DerivedRecord,
            _ when root is not null => EqualityShape.HierarchyDerived,
            { IsSealed: true } => EqualityShape.SealedClass,
            _ => EqualityShape.HierarchyRoot,
        };

        // The properties this type compares itself. A derived type leaves the base's to base.EqualsCore, and so do
        // overrides: the base reads them through the virtual property. A root class also compares what it inherits
        // from plain (non-model) base classes, as the old comparer did; a root record delegates that to base.Equals.
        var properties = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => IsComparable(p) && !(root is not null && p.IsOverride))
            .ToList();
        string? baseRecord = null;
        if (shape == EqualityShape.DerivedRecord)
        {
            baseRecord = type.BaseType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        else if (shape == EqualityShape.Record && type.BaseType is { SpecialType: not SpecialType.System_Object } plainBaseRecord)
        {
            baseRecord = plainBaseRecord.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        else if (shape is EqualityShape.SealedClass or EqualityShape.HierarchyRoot)
        {
            var names = new HashSet<string>(properties.Select(p => p.Name), StringComparer.Ordinal);
            for (var b = type.BaseType; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
                foreach (var p in b.GetMembers().OfType<IPropertySymbol>())
                    if (IsComparable(p) && p.DeclaredAccessibility != Accessibility.Private && names.Add(p.Name))
                        properties.Add(p);
        }

        var planner = new EqualityPlanner(compilation);
        var propertyModels = new List<PropertyDeclaration>();
        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            propertyModels.Add(planner.PlanProperty(type, property, diagnostics));
        }

        var model = new ClassDeclaration
        {
            Namespace = type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
            Parents = parents.ToEquatableArray(),
            Keyword = KeywordOf(type),
            Name = DeclaredName(type),
            FullName = fullName,
            HintName = HintNameOf(type),
            Shape = shape,
            IsSealed = type.IsSealed,
            IsReadOnly = type.IsValueType && (type.IsReadOnly || properties.All(p => p.GetMethod is { IsReadOnly: true })),
            // The root as this type sees it, with its type arguments: the EqualsCore parameter type.
            CoreTypeFullName = shape == EqualityShape.HierarchyDerived ? root!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null,
            BaseRecordFullName = baseRecord,
            Properties = propertyModels.ToEquatableArray(),
            ComparerFields = planner.Fields.ToEquatableArray(),
            // Records and record structs implement IEquatable<T> already; the compiler adds it to their base list.
            AddInterface = shape is not (EqualityShape.Record or EqualityShape.DerivedRecord or EqualityShape.RecordStruct),
        };

        ApplyUserDeclaredMembers(type, model, diagnostics);

        return new DiscoveredType { Model = model, Diagnostics = diagnostics.ToEquatableArray() };
    }

    private static bool IsComparable(IPropertySymbol p)
        => !p.IsStatic
        && !p.IsIndexer
        && !p.IsImplicitlyDeclared
        && p.GetMethod is not null
        && p.ExplicitInterfaceImplementations.IsEmpty
        && p.Name != "EqualityContract";

    /// <summary>D4: members the type declares itself are not generated, and each one skipped is reported.</summary>
    private static void ApplyUserDeclaredMembers(INamedTypeSymbol type, ClassDeclaration model, List<DiagnosticInfo> diagnostics)
    {
        IMethodSymbol? Declared(string name, Func<IMethodSymbol, bool> signature)
            => type.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault(m => !m.IsStatic && !m.IsImplicitlyDeclared && signature(m));

        var equalsT = Declared("Equals", m => m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type));
        var isRecord = type.IsRecord;
        var isDerived = model.Shape is EqualityShape.HierarchyDerived;
        var hasObjectPair = !isRecord && !isDerived;
        var equalsObject = hasObjectPair ? Declared("Equals", m => m.IsOverride && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Object) : null;
        var getHashCode = model.Shape is EqualityShape.HierarchyDerived ? null : Declared("GetHashCode", m => m.Parameters.Length == 0);
        var usesCore = model.Shape is EqualityShape.HierarchyRoot or EqualityShape.HierarchyDerived;
        var equalsCore = usesCore ? Declared("EqualsCore", m => m.Parameters.Length == 1) : null;
        var hashCore = usesCore ? Declared("HashCore", m => m.Parameters.Length == 0) : null;

        model.EmitEqualsT = equalsT is null;
        model.EmitEqualsObject = hasObjectPair && equalsObject is null;
        model.EmitGetHashCode = !isDerived && getHashCode is null;
        model.EmitEqualsCore = usesCore && equalsCore is null;
        model.EmitHashCore = usesCore && hashCore is null;

        void Skipped(IMethodSymbol? member, string text)
        {
            if (member is not null)
                diagnostics.Add(Diagnostic(nameof(ValueComparerDiagnostics.UserDeclaredMember), member.Locations.FirstOrDefault(l => l.IsInSource), type.Name, text, string.Empty));
        }

        Skipped(equalsT, $"Equals({type.Name})");
        Skipped(equalsCore, "EqualsCore");
        Skipped(hashCore, "HashCore()");

        if (hasObjectPair && (equalsObject is null) != (getHashCode is null))
        {
            // Only one half of the pair is the user's: the generated other half can disagree with it.
            var (declared, text, generated) = equalsObject is not null
                ? (equalsObject, "Equals(object)", "GetHashCode()")
                : (getHashCode!, "GetHashCode()", "Equals(object)");
            diagnostics.Add(Diagnostic(ValueComparerDiagnostics.UserDeclaredHalfOfPair, declared.Locations.FirstOrDefault(l => l.IsInSource),
                type.Name, text, $", but it did generate {generated}, so the two can disagree. Declare both, or neither."));
        }
        else
        {
            Skipped(equalsObject, "Equals(object)");
            Skipped(getHashCode, "GetHashCode()");
        }
    }

    public static DiagnosticInfo Diagnostic(string rule, Location? location, params string[] args)
        => new() { Rule = rule, Location = DiagnosticLocation.From(location), MessageArgs = args.ToEquatableArray() };

    private static string KeywordOf(INamedTypeSymbol type) => type switch
    {
        { IsRecord: true, IsValueType: true } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Struct } => "struct",
        { TypeKind: TypeKind.Interface } => "interface",
        _ => "class",
    };

    private static string DeclaredName(INamedTypeSymbol type)
        => type.TypeParameters.Length == 0
            ? Identifier(type.Name)
            : $"{Identifier(type.Name)}<{string.Join(", ", type.TypeParameters.Select(t => Identifier(t.Name)))}>";

    public static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static string HintNameOf(INamedTypeSymbol type)
    {
        var parts = new List<string>();
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
            parts.Insert(0, t.Arity == 0 ? t.Name : $"{t.Name}_{t.Arity}");
        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
            parts.Insert(0, ns.ToDisplayString());
        return string.Join(".", parts) + ".Equality.g.cs";
    }

    /// <summary>Chooses each property's comparison from its type symbol (the D1 table), and collects the comparer fields nesting needs.</summary>
    private sealed class EqualityPlanner(Compilation compilation)
    {
        private const string ValueEquality = "global::MintPlayer.SourceGenerators.Tools.ValueEquality";
        private const string EqualityComparerOpen = "global::System.Collections.Generic.EqualityComparer<";

        private readonly INamedTypeSymbol? immutableArray = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableArray`1");
        private readonly INamedTypeSymbol? readOnlyList = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
        private readonly INamedTypeSymbol? readOnlyDictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        private readonly INamedTypeSymbol? dictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        private readonly INamedTypeSymbol? equalityComparer = compilation.GetTypeByMetadataName("System.Collections.Generic.IEqualityComparer`1");
        private readonly INamedTypeSymbol? equatable = compilation.GetTypeByMetadataName("System.IEquatable`1");

        private readonly List<ComparerField> fields = [];
        private List<ITypeSymbol>? referenceOnly;

        public IReadOnlyList<ComparerField> Fields => fields;

        public PropertyDeclaration PlanProperty(INamedTypeSymbol owner, IPropertySymbol property, List<DiagnosticInfo> diagnostics)
        {
            var result = new PropertyDeclaration { Name = Identifier(property.Name) };
            var attributes = property.GetAttributes();

            if (attributes.Any(a => IsAttribute(a.AttributeClass, "ComparerIgnoreAttribute")))
            {
                result.HasComparerIgnore = true;
                return result;
            }

            var useComparer = attributes.FirstOrDefault(a => IsAttribute(a.AttributeClass, "UseEqualityComparerAttribute"));
            if (useComparer is not null)
            {
                if (TryUseComparer(property, useComparer, out var equals, out var hash, out var error))
                {
                    result.EqualsExpression = equals;
                    result.HashExpression = hash;
                    return result;
                }

                var comparerName = useComparer.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol c ? c.ToDisplayString() : "?";
                diagnostics.Add(Diagnostic(nameof(ValueComparerDiagnostics.InvalidEqualityComparer),
                    useComparer.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault(),
                    comparerName, property.Name, property.Type.ToDisplayString(), error));
                // Falls back to the comparison the type would get, so the rest of the file still compiles.
            }

            referenceOnly = null;
            var (eq, h) = Plan(property.Type);
            result.EqualsExpression = eq;
            result.HashExpression = h;

            if (referenceOnly is { Count: > 0 })
                foreach (var offender in referenceOnly.Distinct<ITypeSymbol>(SymbolEqualityComparer.Default))
                    diagnostics.Add(Diagnostic(nameof(ValueComparerDiagnostics.ReferenceEqualityOnly),
                        property.Locations.FirstOrDefault(l => l.IsInSource), property.Name, owner.Name, offender.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString()));

            return result;
        }

        private bool TryUseComparer(IPropertySymbol property, AttributeData attribute, out string equals, out string hash, out string error)
        {
            equals = hash = error = string.Empty;
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol comparer || comparer.TypeKind == TypeKind.Error)
            {
                error = "the comparer type could not be resolved";
                return false;
            }
            if (comparer.IsUnboundGenericType || comparer.IsAbstract || comparer.IsStatic || comparer.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                error = "it must be a closed, non-abstract class or struct";
                return false;
            }

            var comparedType = comparer.AllInterfaces
                .Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, equalityComparer))
                .Select(i => i.TypeArguments[0])
                .FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t, property.Type) || compilation.HasImplicitConversion(property.Type, t));
            if (comparedType is null)
            {
                error = $"it does not implement IEqualityComparer<{property.Type.ToDisplayString()}>";
                return false;
            }

            var comparerName = comparer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var hasInstance = comparer.GetMembers("Instance").Any(m => m.IsStatic
                && m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
                && m is IFieldSymbol or IPropertySymbol);
            var hasConstructor = comparer.TypeKind == TypeKind.Struct
                || comparer.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);
            if (!hasInstance && !hasConstructor)
            {
                error = "it has neither a static Instance member nor a public parameterless constructor";
                return false;
            }

            var comparerInterface = $"global::System.Collections.Generic.IEqualityComparer<{Display(comparedType)}>";
            var field = AddField(comparerInterface, hasInstance ? $"{comparerName}.Instance" : $"new {comparerName}()");
            equals = $"{field}.Equals($L$, $R$)";
            hash = CanBeNull(property.Type) ? $"($V$ is null ? 0 : {field}.GetHashCode($V$))" : $"{field}.GetHashCode($V$)";
            return true;
        }

        /// <summary>The D1 table: an equality template over <c>$L$</c>/<c>$R$</c> and a hash template over <c>$V$</c>.</summary>
        private (string Eq, string Hash) Plan(ITypeSymbol type)
        {
            var display = Display(type);

            if (type.SpecialType == SpecialType.System_String)
                return ("global::System.String.Equals($L$, $R$, global::System.StringComparison.Ordinal)",
                        "($V$ is null ? 0 : global::System.StringComparer.Ordinal.GetHashCode($V$))");

            if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
                return ("$L$ == $R$", $"(({Display(underlying)})$V$).GetHashCode()");

            if (IsOperatorPrimitive(type.SpecialType))
                return ("$L$ == $R$", "$V$.GetHashCode()");

            // == on floating point makes NaN unequal to itself; Equals keeps equality reflexive, as the old comparer was.
            if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double)
                return ("$L$.Equals($R$)", "$V$.GetHashCode()");

            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return (DefaultEquals(display), "$V$.GetHashCode()");

            if (type is ITypeParameterSymbol)
                return (DefaultEquals(display), DefaultHash(type, display));

            if (type is IArrayTypeSymbol { Rank: 1 } array)
            {
                var element = Display(array.ElementType);
                var comparer = ComparerArgument(array.ElementType);
                return ($"{ValueEquality}.Array<{element}>($L$, $R${comparer})", $"{ValueEquality}.ArrayHash<{element}>($V${comparer})");
            }

            if (type is not INamedTypeSymbol named)
                return (DefaultEquals(display), DefaultHash(type, display));

            if (Is(named, immutableArray))
            {
                var elementType = named.TypeArguments[0];
                var element = Display(elementType);
                var comparer = ComparerArgument(elementType);
                return ($"{ValueEquality}.ImmutableArray<{element}>($L$, $R${comparer})", $"{ValueEquality}.ImmutableArrayHash<{element}>($V${comparer})");
            }

            if (named.IsTupleType)
            {
                var items = named.TupleElements;
                if (items.Length == 0) return ("true", "0");
                var equalsParts = new List<string>();
                var hash = "17";
                for (var i = 0; i < items.Length; i++)
                {
                    var item = $"Item{i + 1}";
                    var (itemEquals, itemHash) = Plan(items[i].Type);
                    equalsParts.Add(itemEquals.Replace("$L$", $"$L$.{item}").Replace("$R$", $"$R$.{item}"));
                    var operand = itemHash.Replace("$V$", $"$V$.{item}");
                    hash = i == 0 ? $"17 * 31 + ({operand})" : $"({hash}) * 31 + ({operand})";
                }
                return ($"({string.Join(" && ", equalsParts)})", $"unchecked({hash})");
            }

            if (HasOwnEquality(named) || IsModel(named))
                return (DefaultEquals(display), DefaultHash(type, display));

            if (Implementation(named, readOnlyDictionary) is { } readOnlyMap)
            {
                var (key, value) = (Display(readOnlyMap.TypeArguments[0]), Display(readOnlyMap.TypeArguments[1]));
                var comparer = ComparerArgument(readOnlyMap.TypeArguments[1]);
                var cast = $"(global::System.Collections.Generic.IReadOnlyDictionary<{key}, {value}>?)";
                return ($"{ValueEquality}.Dictionary<{key}, {value}>({cast}$L$, {cast}$R${comparer})", $"{ValueEquality}.DictionaryHash<{key}, {value}>({cast}$V${comparer})");
            }

            if (Implementation(named, dictionary) is { } map)
            {
                var (key, value) = (Display(map.TypeArguments[0]), Display(map.TypeArguments[1]));
                var comparer = ComparerArgument(map.TypeArguments[1]);
                var cast = $"(global::System.Collections.Generic.IDictionary<{key}, {value}>?)";
                return ($"{ValueEquality}.Dictionary<{key}, {value}>({cast}$L$, {cast}$R${comparer})", $"{ValueEquality}.DictionaryHash<{key}, {value}>({cast}$V${comparer})");
            }

            if (Implementation(named, readOnlyList) is { } list)
            {
                var element = Display(list.TypeArguments[0]);
                var comparer = ComparerArgument(list.TypeArguments[0]);
                return ($"{ValueEquality}.List<{element}>($L$, $R${comparer})", $"{ValueEquality}.ListHash<{element}>($V${comparer})");
            }

            if (Implementation(named, compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T)) is { } sequence)
            {
                var element = Display(sequence.TypeArguments[0]);
                var comparer = ComparerArgument(sequence.TypeArguments[0]);
                return ($"{ValueEquality}.Sequence<{element}>($L$, $R${comparer})", $"{ValueEquality}.SequenceHash<{element}>($V${comparer})");
            }

            if (IsReferenceOnly(named))
                (referenceOnly ??= []).Add(named);

            return (DefaultEquals(display), DefaultHash(type, display));
        }

        /// <summary><c>, field</c> when the element type needs a structural comparer, empty when its default equality is right.</summary>
        private string ComparerArgument(ITypeSymbol element)
            => ElementComparer(element) is { } field ? ", " + field : string.Empty;

        /// <summary>
        /// A field holding the comparer for a collection's element type, or null when
        /// <see cref="EqualityComparer{T}.Default"/> already compares it by value.
        /// </summary>
        private string? ElementComparer(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String
                || type.TypeKind == TypeKind.Enum
                || IsOperatorPrimitive(type.SpecialType)
                || type.SpecialType is SpecialType.System_Single or SpecialType.System_Double
                || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                || type is ITypeParameterSymbol)
                return null;

            var display = Display(type);
            var fieldType = $"global::System.Collections.Generic.IEqualityComparer<{display}>";

            if (type is IArrayTypeSymbol { Rank: 1 } array)
                return AddField(fieldType, Construct("ArrayComparer", Display(array.ElementType), ElementComparer(array.ElementType)));

            if (type is not INamedTypeSymbol named)
                return null;

            if (Is(named, immutableArray))
                return AddField(fieldType, Construct("ImmutableArrayComparer", Display(named.TypeArguments[0]), ElementComparer(named.TypeArguments[0])));

            if (named.IsTupleType)
            {
                // A tuple's own equality compares each item with its default comparer. That is only wrong when an
                // item needs a structural comparison itself; then a delegate comparer inlines the item comparisons.
                if (named.TupleElements.All(e => ElementComparer(e.Type) is null)) return null;
                return DelegateField(type, fieldType);
            }

            if (HasOwnEquality(named) || IsModel(named))
                return null;

            if (Implementation(named, readOnlyDictionary) is { } readOnlyMap)
                return AddField(fieldType, Construct("DictionaryComparer", $"{Display(readOnlyMap.TypeArguments[0])}, {Display(readOnlyMap.TypeArguments[1])}", ElementComparer(readOnlyMap.TypeArguments[1])));

            if (Implementation(named, dictionary) is not null)
                return DelegateField(type, fieldType);

            if (Implementation(named, readOnlyList) is { } list)
                return AddField(fieldType, Construct("ListComparer", Display(list.TypeArguments[0]), ElementComparer(list.TypeArguments[0])));

            if (Implementation(named, compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T)) is { } sequence)
                return AddField(fieldType, Construct("SequenceComparer", Display(sequence.TypeArguments[0]), ElementComparer(sequence.TypeArguments[0])));

            if (IsReferenceOnly(named))
                (referenceOnly ??= []).Add(named);

            return null;
        }

        private string DelegateField(ITypeSymbol type, string fieldType)
        {
            var (equals, hash) = Plan(type);
            return AddField(fieldType, $"new {ValueEquality}.DelegateComparer<{Display(type)}>((x, y) => {equals.Replace("$L$", "x").Replace("$R$", "y")}, x => {hash.Replace("$V$", "x")})");
        }

        private static string Construct(string comparer, string typeArguments, string? inner)
            => inner is null
                ? $"{ValueEquality}.{comparer}<{typeArguments}>.Instance"
                : $"new {ValueEquality}.{comparer}<{typeArguments}>({inner})";

        private string AddField(string type, string initializer)
        {
            foreach (var existing in fields)
                if (existing.Type == type && existing.Initializer == initializer)
                    return existing.Name;

            var name = $"s_valueEquality{fields.Count}";
            fields.Add(new ComparerField { Name = name, Type = type, Initializer = initializer });
            return name;
        }

        private static string DefaultEquals(string display) => $"{EqualityComparerOpen}{display}>.Default.Equals($L$, $R$)";

        private static string DefaultHash(ITypeSymbol type, string display)
            => CanBeNull(type)
                ? $"($V$ is null ? 0 : {EqualityComparerOpen}{display}>.Default.GetHashCode($V$))"
                : $"{EqualityComparerOpen}{display}>.Default.GetHashCode($V$)";

        private static bool CanBeNull(ITypeSymbol type)
            => !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? definition)
            => definition is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition);

        /// <summary>The type itself when it is the constructed <paramref name="definition"/>, else the first such interface it implements.</summary>
        private static INamedTypeSymbol? Implementation(INamedTypeSymbol type, INamedTypeSymbol? definition)
        {
            if (definition is null) return null;
            if (Is(type, definition)) return type;
            return type.AllInterfaces.FirstOrDefault(i => Is(i, definition));
        }

        /// <summary>Records, and types that implement <c>IEquatable&lt;T&gt;</c> of themselves or override <c>Equals(object)</c>.</summary>
        private bool HasOwnEquality(INamedTypeSymbol type)
        {
            if (type.IsRecord) return true;
            if (type.TypeKind == TypeKind.Interface) return false;
            if (type.AllInterfaces.Any(i => Is(i, equatable) && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type)))
                return true;
            for (var b = type; b is not null && b.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); b = b.BaseType)
                if (b.GetMembers("Equals").OfType<IMethodSymbol>().Any(m => m.IsOverride && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Object))
                    return true;
            return false;
        }

        /// <summary>MINT005: a class compared by reference, which never caches.</summary>
        private bool IsReferenceOnly(INamedTypeSymbol type)
            => type.TypeKind == TypeKind.Class
            && type.SpecialType is not (SpecialType.System_Object or SpecialType.System_String)
            && !type.IsRecord
            && !IsModel(type)
            && !HasOwnEquality(type);

        private static bool IsOperatorPrimitive(SpecialType type) => type is
            SpecialType.System_Boolean or SpecialType.System_Char or
            SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Decimal or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

        private static string Display(ITypeSymbol type) => type.ToDisplayString(TypeFormat);
    }
}
