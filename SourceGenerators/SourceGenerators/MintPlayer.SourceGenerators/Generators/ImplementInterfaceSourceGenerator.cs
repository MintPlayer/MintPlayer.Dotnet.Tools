using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Generators;

/// <summary>
/// <c>[ImplementInterface(typeof(IA), typeof(IB))]</c> (or <c>[ImplementInterface&lt;IA&gt;]</c>) on a partial type: adds
/// the interfaces to its base list and implements every member that is still unimplemented and has an obvious body.
/// Read/write properties become auto-properties; on an abstract class every other instance member becomes
/// <c>public abstract</c>; on a concrete type those members are IMPLINTF003/IMPLINTF004 unless written by hand.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class ImplementInterfaceSourceGenerator : IncrementalGenerator
{
    /// <summary>The metadata name of <c>MintPlayer.SourceGenerators.Attributes.ImplementInterfaceAttribute</c>.</summary>
    public const string AttributeMetadataName = "MintPlayer.SourceGenerators.Attributes.ImplementInterfaceAttribute";

    /// <summary>The metadata name of <c>MintPlayer.SourceGenerators.Attributes.ImplementInterfaceAttribute&lt;TInterface&gt;</c>.</summary>
    public const string GenericAttributeMetadataName = "MintPlayer.SourceGenerators.Attributes.ImplementInterfaceAttribute`1";

    private const string AttributeNamespace = "MintPlayer.SourceGenerators.Attributes";
    private const string AttributeName = "ImplementInterfaceAttribute";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var plain = context.SyntaxProvider.ForAttribute(
            AttributeMetadataName,
            static (node, ct) => node is TypeDeclarationSyntax,
            static (ctx, ct) => Discover(ctx, generic: false, ct));
        var generic = context.SyntaxProvider.ForAttribute(
            GenericAttributeMetadataName,
            static (node, ct) => node is TypeDeclarationSyntax,
            static (ctx, ct) => Discover(ctx, generic: true, ct));

        var typesSourceProvider = plain.Models().Collect()
            .Combine(generic.Models().Collect())
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new ImplementInterfaceProducer(
                providers.Item1.Concat(providers.Item2), providers.Item3.RootNamespace!));

        context.ProduceCode(typesSourceProvider);
        context.ReportDiagnostics(plain.Diagnostics(), generic.Diagnostics());
    }

    private sealed class Request(INamedTypeSymbol @interface, bool @explicit, bool initializeNonNullable, Location? location)
    {
        public INamedTypeSymbol Interface => @interface;
        public bool Explicit => @explicit;
        public bool InitializeNonNullable => initializeNonNullable;
        public Location? Location => location;
    }

    /// <summary>A member declared so far, by its collision key: its text, and the interface member it implements.</summary>
    private sealed class Declared(string text, ISymbol member)
    {
        public string Text => text;
        public ISymbol Member => member;
    }

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat MessageFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    private static Discovered<Models.ImplementInterfaceDeclaration>? Discover(GeneratorAttributeSyntaxContext ctx, bool generic, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type || ctx.TargetNode is not TypeDeclarationSyntax declaration) return null;

        // Every attributed partial part, and each attribute form, triggers a call. The call for the part and form of the
        // type's first attribute speaks for the whole type, so the type is declared once and diagnostics are not repeated.
        var attributes = type.GetAttributes()
            .Where(a => a.AttributeClass is { Name: AttributeName } c && c.ContainingNamespace.ToDisplayString() == AttributeNamespace)
            .ToArray();
        if (attributes.Length == 0) return null;
        var first = attributes[0];
        var firstOwner = first.ApplicationSyntaxReference?.GetSyntax(ct).FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (first.AttributeClass!.IsGenericType != generic
            || firstOwner is null
            || firstOwner.SyntaxTree != declaration.SyntaxTree
            || firstOwner.Span != declaration.Span)
            return null;

        var typeDisplay = type.ToDisplayString(MessageFormat);
        var typeLocation = declaration.Identifier.GetLocation();

        if (TargetProblem(type) is { } problem)
            return Discovered<Models.ImplementInterfaceDeclaration>.Fail(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceUnsupportedTarget, typeLocation, typeDisplay, problem));

        var pathSpec = type.GetPathSpec(ct);
        if (pathSpec is null) return null;
        if (!pathSpec.IsSupported)
        {
            return Discovered<Models.ImplementInterfaceDeclaration>.Fail(pathSpec.UnsupportedReason switch
            {
                EPathSpecUnsupportedReason.FileLocal => DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceUnsupportedTarget, typeLocation, typeDisplay, "is file-local, so a generated partial would be another type"),
                EPathSpecUnsupportedReason.UnsupportedKind => DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceUnsupportedTarget, typeLocation, typeDisplay, "is not a class, struct or record"),
                _ => DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceTypeNotPartial, typeLocation, typeDisplay, pathSpec.UnsupportedTypeName),
            });
        }

        var diagnostics = new List<DiagnosticInfo>();
        var requests = CollectRequests(type, attributes, typeDisplay, diagnostics, ct);

        var builder = new MemberBuilder(type, typeDisplay, requests, diagnostics);
        builder.Build(ct);

        var baseList = requests
            .Select(r => r.Interface)
            .Where(i => !type.AllInterfaces.Contains(i, SymbolEqualityComparer.Default))
            .Select(i => i.ToDisplayString(TypeFormat))
            .ToArray();

        if (baseList.Length == 0 && builder.Members.Count == 0)
            return new Discovered<Models.ImplementInterfaceDeclaration>(null, diagnostics.ToEquatableArray());

        // Emitted even when a member was reported: the rest of the consumer's code keeps compiling against the
        // interfaces, and the build fails on our error rather than on a cascade of conversions (S4).
        return new Discovered<Models.ImplementInterfaceDeclaration>(new Models.ImplementInterfaceDeclaration
        {
            PathSpec = pathSpec,
            BaseList = baseList,
            Members = [.. builder.Members],
        }, diagnostics.ToEquatableArray());
    }

    private static string? TargetProblem(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface) return "is an interface; list the interfaces in its base list instead";
        if (type.IsStatic) return "is a static class, which cannot implement interfaces";
        return null;
    }

    private static List<Request> CollectRequests(INamedTypeSymbol type, AttributeData[] attributes, string typeDisplay, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var requests = new List<Request>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in attributes)
        {
            var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(ct) as AttributeSyntax;
            var attributeLocation = syntax?.GetLocation();
            var isExplicit = attribute.GetArgument("Explicit", false);
            var initialize = attribute.GetArgument("InitializeNonNullable", true);

            foreach (var (argument, location) in InterfaceArguments(attribute, syntax))
            {
                if (argument is null or { TypeKind: TypeKind.Error }) continue;

                var argumentLocation = location ?? attributeLocation;
                if (argument is INamedTypeSymbol { IsUnboundGenericType: true })
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceNotAnInterface, argumentLocation, argument.ToDisplayString(MessageFormat), "is an open generic type; name its type arguments"));
                    continue;
                }
                if (argument is not INamedTypeSymbol { TypeKind: TypeKind.Interface } @interface)
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceNotAnInterface, argumentLocation, argument.ToDisplayString(MessageFormat), $"is {Article(argument.TypeKind.ToPlaintext() ?? "type")}, not an interface"));
                    continue;
                }
                if (!seen.Add(@interface))
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceDuplicate, argumentLocation, @interface.ToDisplayString(MessageFormat), typeDisplay));
                    continue;
                }
                requests.Add(new Request(@interface, isExplicit, initialize, attributeLocation));
            }
        }
        return requests;
    }

    /// <summary>The interface arguments of one attribute, each with the location of its <c>typeof</c> when it has one.</summary>
    private static IEnumerable<(ITypeSymbol? Type, Location? Location)> InterfaceArguments(AttributeData attribute, AttributeSyntax? syntax)
    {
        if (attribute.AttributeClass!.IsGenericType)
        {
            var location = (syntax?.Name as GenericNameSyntax)?.TypeArgumentList.Arguments.FirstOrDefault()?.GetLocation()
                ?? (syntax?.Name as QualifiedNameSyntax)?.Right.GetLocation();
            yield return (attribute.AttributeClass.TypeArguments[0], location);
            yield break;
        }

        if (attribute.ConstructorArguments.Length != 1) yield break;
        var argument = attribute.ConstructorArguments[0];
        var values = argument.Kind == TypedConstantKind.Array ? argument.Values : [argument];

        // `params` arguments map one to one onto the positional arguments; an explicit array does not.
        var positional = syntax?.ArgumentList?.Arguments.Where(a => a.NameEquals is null).ToArray() ?? [];
        for (var i = 0; i < values.Length; i++)
        {
            var location = positional.Length == values.Length ? positional[i].GetLocation() : null;
            yield return (values[i].Value as ITypeSymbol, location);
        }
    }

    private static string Article(string noun) => noun.Length > 0 && "aeiou".IndexOf(noun[0]) >= 0 ? $"an {noun}" : $"a {noun}";

    /// <summary>Decides, per interface member, whether it is implemented already, generated, or reported.</summary>
    private sealed class MemberBuilder(INamedTypeSymbol type, string typeDisplay, List<Request> requests, List<DiagnosticInfo> diagnostics)
    {
        private readonly bool isAbstractTarget = type.TypeKind == TypeKind.Class && type.IsAbstract;
        private readonly bool isStruct = type.TypeKind == TypeKind.Struct;
        private readonly int typeAccessibility = EffectiveAccessibility(type);

        /// <summary>Public members generated so far, by name for properties and events, by signature for methods.</summary>
        private readonly Dictionary<string, Declared> declared = new(StringComparer.Ordinal);

        public List<string> Members { get; } = [];

        public void Build(CancellationToken ct)
        {
            var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var allMembers = new List<(ISymbol Member, Request Request)>();
            foreach (var request in requests)
            {
                foreach (var @interface in new[] { request.Interface }.Concat(request.Interface.AllInterfaces))
                {
                    if (!processed.Add(@interface)) continue;
                    foreach (var member in @interface.GetMembers())
                    {
                        if (!member.IsAbstract) continue; // default implementations, static virtual members with a body
                        if (member is IMethodSymbol { MethodKind: not (MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion) }) continue; // accessors
                        if (member is not (IPropertySymbol or IMethodSymbol or IEventSymbol)) continue;
                        allMembers.Add((member, request));
                    }
                }
            }

            foreach (var (member, request) in allMembers)
            {
                ct.ThrowIfCancellationRequested();
                Process(member, request, allMembers);
            }
        }

        private void Process(ISymbol member, Request request, List<(ISymbol Member, Request Request)> allMembers)
        {
            var memberDisplay = member.ToDisplayString(MessageFormat);
            var forceExplicit = false;

            if (IsImplemented(member, out var mismatch, out var mismatchReason)) return;
            if (mismatch is not null)
            {
                // A hand-written property that implements a same-named property of another requested interface is the
                // collision of R2.7, not a mistake: the other interface gets the explicit implementation.
                var implementsOther = member is IPropertySymbol && mismatch is IPropertySymbol written
                    && allMembers.Any(o => o.Member is IPropertySymbol other && other.Name == member.Name && SameType(other.Type, written.Type));
                if (!implementsOther || !IsReadWrite(member) || member.IsStatic)
                {
                    var location = mismatch.Locations.FirstOrDefault(l => l.IsInSource) ?? request.Location;
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceMemberMismatch, location, mismatch.ToDisplayString(MessageFormat), mismatchReason, memberDisplay));
                    return;
                }
                forceExplicit = true;
            }

            if (member is IPropertySymbol property && IsReadWrite(property))
            {
                GenerateProperty(property, request, memberDisplay, forceExplicit);
                return;
            }

            if (member.IsStatic)
            {
                Report(DiagnosticRules.ImplementInterfaceUnsupportedMember, request, memberDisplay, $"a static {KindOf(member)}", string.Empty);
                return;
            }

            if (!isAbstractTarget || request.Explicit)
            {
                var suffix = isAbstractTarget ? ", because an explicit implementation (Explicit = true) cannot be abstract" : string.Empty;
                if (member is IPropertySymbol { IsIndexer: false } p)
                    Report(DiagnosticRules.ImplementInterfaceUnsupportedProperty, request, memberDisplay, PropertyShape(p), suffix);
                else
                    Report(DiagnosticRules.ImplementInterfaceUnsupportedMember, request, memberDisplay, Article(KindOf(member)), suffix);
                return;
            }

            var (key, text) = AbstractDeclaration(member);
            Declare(key, text, member, request);
        }

        private void Report(DiagnosticDescriptor rule, Request request, string memberDisplay, string kind, string suffix)
            => diagnostics.Add(DiagnosticInfo.Create(rule, request.Location, memberDisplay, kind, suffix, typeDisplay));

        private void GenerateProperty(IPropertySymbol property, Request request, string memberDisplay, bool forceExplicit)
        {
            var propertyType = property.Type.ToDisplayString(TypeFormat);
            var accessors = property.SetMethod!.IsInitOnly ? "{ get; init; }" : "{ get; set; }";
            var initializer = request.InitializeNonNullable
                && property.Type.IsReferenceType
                && property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated
                && (!isStruct || property.IsStatic)
                ? " = default!;" : string.Empty;
            var name = SymbolExtensions.EscapeIdentifier(property.Name);

            // A property type less accessible than the type cannot be public (CS0053), so it is implemented explicitly (S5).
            var isExplicit = !property.IsStatic
                && (request.Explicit || forceExplicit || EffectiveAccessibility(property.Type) < typeAccessibility);

            if (!isExplicit)
            {
                var text = Wrap($"public {(property.IsStatic ? "static " : string.Empty)}{propertyType} {name} {accessors}{initializer}", property.Type);
                if (declared.TryGetValue(Key(property), out var existing))
                {
                    if (existing.Text == text) return; // one public property satisfies both interfaces
                    if (property.IsStatic || existing.Member is not IPropertySymbol { IsIndexer: false } || !IsReadWrite(existing.Member))
                    {
                        diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceConflict, request.Location, existing.Member.ToDisplayString(MessageFormat), memberDisplay, typeDisplay));
                        return;
                    }
                    isExplicit = true; // R2.7: the interface listed first keeps the public property
                }
                else
                {
                    Declare(Key(property), text, property, request);
                    return;
                }
            }

            var interfaceName = property.ContainingType.ToDisplayString(TypeFormat);
            Members.Add(Wrap($"{propertyType} {interfaceName}.{name} {accessors}{initializer}", property.Type));
        }

        private void Declare(string key, string text, ISymbol member, Request request)
        {
            if (declared.TryGetValue(key, out var existing))
            {
                if (existing.Text != text)
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceConflict, request.Location, existing.Member.ToDisplayString(MessageFormat), member.ToDisplayString(MessageFormat), typeDisplay));
                return;
            }

            // A method and a property of one name cannot both be declared (CS0102).
            var nameKey = "N:" + member.Name;
            var isMethod = member is IMethodSymbol;
            if (declared.TryGetValue(nameKey, out var sameName) && (sameName.Member is IMethodSymbol) != isMethod)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.ImplementInterfaceConflict, request.Location, sameName.Member.ToDisplayString(MessageFormat), member.ToDisplayString(MessageFormat), typeDisplay));
                return;
            }

            declared[key] = new Declared(text, member);
            if (!declared.ContainsKey(nameKey)) declared[nameKey] = new Declared(text, member);
            Members.Add(text);
        }

        private static string Key(ISymbol member) => member switch
        {
            IMethodSymbol m => $"M:{m.Name}`{m.Arity}({ParameterKey(m.Parameters)})",
            IPropertySymbol { IsIndexer: true } p => $"I:({ParameterKey(p.Parameters)})",
            _ => "P:" + member.Name,
        };

        private static string ParameterKey(IEnumerable<IParameterSymbol> parameters)
            => string.Join(",", parameters.Select(p => p.RefKind + " " + p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        private (string Key, string Text) AbstractDeclaration(ISymbol member)
        {
            switch (member)
            {
                case IMethodSymbol method:
                {
                    var returnType = method.ReturnsVoid ? "void" : RefPrefix(method.RefKind) + method.ReturnType.ToDisplayString(TypeFormat);
                    var typeParameters = method.TypeParameters.Length == 0 ? string.Empty
                        : $"<{string.Join(", ", method.TypeParameters.Select(tp => SymbolExtensions.EscapeIdentifier(tp.Name)))}>";
                    var text = $"public abstract {returnType} {SymbolExtensions.EscapeIdentifier(method.Name)}{typeParameters}({Parameters(method.Parameters)}){Constraints(method.TypeParameters)};";
                    return (Key(method), Wrap(text, SignatureTypes(method)));
                }
                case IPropertySymbol property:
                {
                    var propertyType = RefPrefix(property.RefKind) + property.Type.ToDisplayString(TypeFormat);
                    var head = property.IsIndexer ? $"this[{Parameters(property.Parameters)}]" : SymbolExtensions.EscapeIdentifier(property.Name);
                    var text = $"public abstract {propertyType} {head} {Accessors(property)}";
                    return (Key(property), Wrap(text, [property.Type, .. property.Parameters.Select(p => p.Type)]));
                }
                case IEventSymbol @event:
                {
                    var text = $"public abstract event {@event.Type.ToDisplayString(TypeFormat)} {SymbolExtensions.EscapeIdentifier(@event.Name)};";
                    return ("E:" + @event.Name, Wrap(text, @event.Type));
                }
                default:
                    throw new InvalidOperationException($"Unexpected member kind {member.Kind}");
            }
        }

        private static IEnumerable<ITypeSymbol> SignatureTypes(IMethodSymbol method)
            => (method.ReturnsVoid ? [] : new[] { method.ReturnType }).Concat(method.Parameters.Select(p => p.Type));

        /// <summary>
        /// Whether <paramref name="member"/> is implemented by a hand-written or inherited member. When a member of the
        /// same name is there but does not fit, <paramref name="mismatch"/> is that member and
        /// <paramref name="reason"/> says how it differs.
        /// </summary>
        private bool IsImplemented(ISymbol member, out ISymbol? mismatch, out string reason)
        {
            mismatch = null;
            reason = string.Empty;

            if (type.AllInterfaces.Contains(member.ContainingType, SymbolEqualityComparer.Default)
                && type.FindImplementationForInterfaceMember(member) is not null)
                return true;

            // An explicit implementation written by hand, of an interface that only the generator adds to the base list.
            if (type.GetMembers().Any(m => ExplicitImplementations(m).Contains(member, SymbolEqualityComparer.Default)))
                return true;

            var name = member is IPropertySymbol { IsIndexer: true } ? WellKnownMemberNames.Indexer : member.Name;
            foreach (var candidate in type.GetAllBaseTypes().SelectMany(t => t.GetMembers(name)))
            {
                if (candidate.IsStatic != member.IsStatic || candidate.IsImplicitlyDeclared && candidate is not IPropertySymbol) continue;

                switch (member, candidate)
                {
                    case (IPropertySymbol p, IPropertySymbol c) when !p.IsIndexer || SameParameters(p.Parameters, c.Parameters):
                        if (c.DeclaredAccessibility != Accessibility.Public)
                            return Mismatch(c, $"{Describe(c.DeclaredAccessibility)} instead of public", out mismatch, out reason);
                        if (SameType(p.Type, c.Type) && p.RefKind == c.RefKind) return true;
                        return Mismatch(c, $"'{c.Type.ToDisplayString(MessageFormat)}' instead of '{p.Type.ToDisplayString(MessageFormat)}'", out mismatch, out reason);
                    case (IEventSymbol e, IEventSymbol c):
                        if (c.DeclaredAccessibility != Accessibility.Public)
                            return Mismatch(c, $"{Describe(c.DeclaredAccessibility)} instead of public", out mismatch, out reason);
                        if (SameType(e.Type, c.Type)) return true;
                        return Mismatch(c, $"'{c.Type.ToDisplayString(MessageFormat)}' instead of '{e.Type.ToDisplayString(MessageFormat)}'", out mismatch, out reason);
                    case (IMethodSymbol m, IMethodSymbol c) when c.MethodKind == m.MethodKind && c.Arity == m.Arity && SameParameters(m.Parameters, c.Parameters):
                        if (c.DeclaredAccessibility != Accessibility.Public)
                            return Mismatch(c, $"{Describe(c.DeclaredAccessibility)} instead of public", out mismatch, out reason);
                        if (SameType(m.ReturnType, c.ReturnType) && m.RefKind == c.RefKind) return true;
                        return Mismatch(c, $"'{c.ReturnType.ToDisplayString(MessageFormat)}' instead of '{m.ReturnType.ToDisplayString(MessageFormat)}'", out mismatch, out reason);
                }
            }

            // Another kind of member of the same name on the type itself (a field, a method for a property) would clash
            // with the generated member (CS0102). Methods overload, so a method only clashes with a non-method.
            var clash = type.GetMembers(name).FirstOrDefault(m => !m.IsImplicitlyDeclared && (m is not IMethodSymbol || member is not IMethodSymbol));
            if (clash is not null)
                return Mismatch(clash, $"{Article(KindOf(clash))} instead of {Article(KindOf(member))}", out mismatch, out reason);
            return false;
        }

        private static bool Mismatch(ISymbol candidate, string why, out ISymbol? mismatch, out string reason)
        {
            mismatch = candidate;
            reason = why;
            return false;
        }

        private static IEnumerable<ISymbol> ExplicitImplementations(ISymbol member) => member switch
        {
            IPropertySymbol p => p.ExplicitInterfaceImplementations,
            IMethodSymbol m => m.ExplicitInterfaceImplementations,
            IEventSymbol e => e.ExplicitInterfaceImplementations,
            _ => [],
        };

        private string Wrap(string text, ITypeSymbol type) => Wrap(text, [type]);

        /// <summary>
        /// A declaration whose types come from a nullable-oblivious context stays oblivious, so the generated member
        /// never claims more than the interface does. <c>enable</c> rather than <c>restore</c> afterwards: restore
        /// returns to the project's setting, not to the file's <c>#nullable enable</c>.
        /// </summary>
        private static string Wrap(string text, IEnumerable<ITypeSymbol> types)
            => types.Any(t => t.IsReferenceType && t.NullableAnnotation == NullableAnnotation.None)
                ? $"#nullable disable annotations\n{text}\n#nullable enable annotations"
                : text;
    }

    private static bool IsReadWrite(ISymbol member)
        => member is IPropertySymbol { IsIndexer: false, RefKind: RefKind.None, GetMethod: { } get, SetMethod: { } set }
            && get.DeclaredAccessibility == Accessibility.Public
            && set.DeclaredAccessibility == Accessibility.Public;

    private static string PropertyShape(IPropertySymbol property)
    {
        if (property.RefKind != RefKind.None) return "a ref-returning";
        if (property.SetMethod is null) return "a get-only";
        if (property.GetMethod is null) return "a set-only";
        return "an accessor-restricted";
    }

    private static string KindOf(ISymbol member) => member switch
    {
        IPropertySymbol { IsIndexer: true } => "indexer",
        IPropertySymbol => "property",
        IEventSymbol => "event",
        IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } => "operator",
        IMethodSymbol => "method",
        IFieldSymbol => "field",
        INamedTypeSymbol => "nested type",
        _ => member.Kind.ToString().ToLowerInvariant(),
    };

    private static string Describe(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => accessibility.ToString().ToLowerInvariant(),
    };

    private static bool SameType(ITypeSymbol a, ITypeSymbol b) => SameTypeMapped(a, b);

    /// <summary>
    /// Type equality ignoring nullability, where method type parameters match by position: an implementation's
    /// <c>M&lt;U&gt;(U)</c> implements <c>M&lt;T&gt;(T)</c>.
    /// </summary>
    private static bool SameTypeMapped(ITypeSymbol a, ITypeSymbol b)
    {
        switch (a, b)
        {
            case (ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } ta, ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } tb):
                return ta.Ordinal == tb.Ordinal;
            case (IArrayTypeSymbol aa, IArrayTypeSymbol ab):
                return aa.Rank == ab.Rank && SameTypeMapped(aa.ElementType, ab.ElementType);
            case (INamedTypeSymbol { IsGenericType: true } na, INamedTypeSymbol { IsGenericType: true } nb):
                return SymbolEqualityComparer.Default.Equals(na.OriginalDefinition, nb.OriginalDefinition)
                    && na.TypeArguments.Length == nb.TypeArguments.Length
                    && na.TypeArguments.Zip(nb.TypeArguments, SameTypeMapped).All(x => x);
            default:
                return SymbolEqualityComparer.Default.Equals(a, b);
        }
    }

    private static bool SameParameters(IReadOnlyList<IParameterSymbol> a, IReadOnlyList<IParameterSymbol> b)
        => a.Count == b.Count && a.Zip(b, (x, y) => x.RefKind == y.RefKind && SameTypeMapped(x.Type, y.Type)).All(x => x);

    /// <summary>A rank for "how widely visible": a member cannot be more visible than the types in its signature.</summary>
    private static int EffectiveAccessibility(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array: return EffectiveAccessibility(array.ElementType);
            case IPointerTypeSymbol pointer: return EffectiveAccessibility(pointer.PointedAtType);
            case INamedTypeSymbol named:
            {
                var rank = int.MaxValue;
                for (ISymbol? s = named; s is INamedTypeSymbol t; s = t.ContainingType)
                    rank = Math.Min(rank, Rank(t.DeclaredAccessibility));
                foreach (var argument in named.TypeArguments)
                    rank = Math.Min(rank, EffectiveAccessibility(argument));
                return rank;
            }
            default: return Rank(Accessibility.Public);
        }

        static int Rank(Accessibility accessibility) => accessibility switch
        {
            Accessibility.Private => 0,
            Accessibility.ProtectedAndInternal => 1,
            Accessibility.Internal or Accessibility.Protected => 2,
            Accessibility.ProtectedOrInternal => 3,
            _ => 4,
        };
    }

    private static string RefPrefix(RefKind refKind) => refKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.RefReadOnly => "ref readonly ",
        _ => string.Empty,
    };

    private static string Accessors(IPropertySymbol property)
    {
        var get = property.GetMethod is not null ? "get; " : string.Empty;
        var set = property.SetMethod is null ? string.Empty : property.SetMethod.IsInitOnly ? "init; " : "set; ";
        return $"{{ {get}{set}}}";
    }

    private static string Parameters(IEnumerable<IParameterSymbol> parameters)
        => string.Join(", ", parameters.Select(Parameter));

    private static string Parameter(IParameterSymbol parameter)
    {
        var scoped = parameter.ScopedKind == ScopedKind.ScopedRef && parameter.RefKind != RefKind.Out ? "scoped " : string.Empty;
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => string.Empty,
        };
        var @params = parameter.IsParams ? "params " : string.Empty;
        var defaultValue = parameter.HasExplicitDefaultValue ? " = " + DefaultValue(parameter.ExplicitDefaultValue, parameter.Type) : string.Empty;
        return $"{@params}{scoped}{modifier}{parameter.Type.ToDisplayString(TypeFormat)} {SymbolExtensions.EscapeIdentifier(parameter.Name)}{defaultValue}";
    }

    private static string DefaultValue(object? value, ITypeSymbol type)
    {
        var isNullableValueType = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var underlying = isNullableValueType ? ((INamedTypeSymbol)type).TypeArguments[0] : type;

        if (value is null)
            return type.IsValueType && !isNullableValueType ? "default" : "null";
        if (underlying.TypeKind == TypeKind.Enum)
            return $"({underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false)})";

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            double d when double.IsNaN(d) => "double.NaN",
            double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
            double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            float f when float.IsNaN(f) => "float.NaN",
            float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
            float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "F",
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "D",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            _ => $"({underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}",
        };
    }

    private static string Constraints(IEnumerable<ITypeParameterSymbol> typeParameters)
    {
        var clauses = new List<string>();
        foreach (var tp in typeParameters)
        {
            var parts = new List<string>();
            if (tp.HasReferenceTypeConstraint)
                parts.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (tp.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            else if (tp.HasValueTypeConstraint)
                parts.Add("struct");
            else if (tp.HasNotNullConstraint)
                parts.Add("notnull");
            parts.AddRange(tp.ConstraintTypes.Select(t => t.ToDisplayString(TypeFormat)));
            if (tp.HasConstructorConstraint && !tp.HasValueTypeConstraint)
                parts.Add("new()");
            if (tp.AllowsRefLikeType)
                parts.Add("allows ref struct");
            if (parts.Count > 0)
                clauses.Add($" where {SymbolExtensions.EscapeIdentifier(tp.Name)} : {string.Join(", ", parts)}");
        }
        return string.Concat(clauses);
    }
}
