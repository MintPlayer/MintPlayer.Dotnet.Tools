using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class InjectSourceGenerator : IncrementalGenerator
{
    private const string AttributesNamespace = "MintPlayer.SourceGenerators.Attributes.";
    internal const string InjectAttributeName = AttributesNamespace + "InjectAttribute";
    internal const string PostConstructAttributeName = AttributesNamespace + "PostConstructAttribute";
    internal const string ConfigAttributeName = AttributesNamespace + "ConfigAttribute";
    internal const string ConnectionStringAttributeName = AttributesNamespace + "ConnectionStringAttribute";
    internal const string OptionsAttributeName = AttributesNamespace + "OptionsAttribute";

    private const string IConfigurationType = "global::Microsoft.Extensions.Configuration.IConfiguration";

    /// <summary>The attributes that opt a type in. Any one of them on a member makes the type a target.</summary>
    private static readonly string[] MarkerAttributeNames =
    [
        InjectAttributeName,
        PostConstructAttributeName,
        ConfigAttributeName,
        ConnectionStringAttributeName,
        OptionsAttributeName,
    ];

    /// <summary>Parameter types as written in the constructor: keeps the <c>?</c> of a nullable reference type.</summary>
    private static readonly SymbolDisplayFormat ParameterTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The format <see cref="SymbolExtensions.HasMatchingConstructor"/> compares with.</summary>
    private static readonly SymbolDisplayFormat MatchTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);

    // Measure performance of the Analyzer
    // https://www.meziantou.net/measuring-performance-of-roslyn-source-generators.htm
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        // One provider per marker attribute: ForAttributeWithMetadataName only visits members that carry it, so
        // unrelated code (a [GeneratedRegex] in a record, say) never reaches a transform. Each transform discovers
        // the whole type, but only from the type's first marked member, so a type is discovered once however many
        // members and partial parts carry markers (W4).
        var discovered = context.SyntaxProvider.ForAttribute<Models.ClassWithBaseDependenciesAndInjectFields>(InjectAttributeName, IsMember, DiscoverFromMember).Collect();
        discovered = Concat(discovered, context.SyntaxProvider.ForAttribute<Models.ClassWithBaseDependenciesAndInjectFields>(PostConstructAttributeName, IsMember, DiscoverFromMember));
        discovered = Concat(discovered, context.SyntaxProvider.ForAttribute<Models.ClassWithBaseDependenciesAndInjectFields>(ConfigAttributeName, IsMember, DiscoverFromMember));
        discovered = Concat(discovered, context.SyntaxProvider.ForAttribute<Models.ClassWithBaseDependenciesAndInjectFields>(ConnectionStringAttributeName, IsMember, DiscoverFromMember));
        discovered = Concat(discovered, context.SyntaxProvider.ForAttribute<Models.ClassWithBaseDependenciesAndInjectFields>(OptionsAttributeName, IsMember, DiscoverFromMember));

        // A partial class without markers of its own still gets a constructor that forwards its base class's
        // dependencies. No attribute marks it, so this one provider is syntax-driven.
        discovered = Concat(discovered, context.SyntaxProvider
            .CreateSyntaxProvider(IsPartialDerivedType, DiscoverFromDerivedType)
            .Where(static d => d is not null)
            .Select(static (d, ct) => d!));

        // A member carrying two markers is discovered by two providers; the results are equal, so Distinct
        // leaves one. The models are compared by value, so an edit that changes no model keeps the output cached.
        var models = discovered.Select(static (all, ct) => all
            .Select(d => d.Model)
            .Where(m => m is not null)
            .Select(m => m!)
            .Distinct()
            .ToEquatableArray());

        var diagnostics = discovered.SelectMany(static (all, ct) => all
            .SelectMany(d => d.Diagnostics)
            .Distinct()
            .ToImmutableArray());

        var classesSourceProvider = models
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new InjectProducer(providers.Item1, providers.Item2.RootNamespace!));

        context.ProduceCode(classesSourceProvider);
        context.ReportDiagnostics(diagnostics);
    }

    private static IncrementalValueProvider<ImmutableArray<T>> Concat<T>(IncrementalValueProvider<ImmutableArray<T>> left, IncrementalValuesProvider<T> right)
        => left.Combine(right.Collect()).Select(static (pair, ct) => pair.Left.AddRange(pair.Right));

    private static bool IsMember(SyntaxNode node, CancellationToken ct)
        => node is VariableDeclaratorSyntax or FieldDeclarationSyntax or PropertyDeclarationSyntax or MethodDeclarationSyntax;

    private static bool IsPartialDerivedType(SyntaxNode node, CancellationToken ct)
        => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 } type
            // A struct has no base class to forward to.
            && (node is ClassDeclarationSyntax || (node is RecordDeclarationSyntax record && !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)))
            && type.Modifiers.Any(SyntaxKind.PartialKeyword);

    private static Discovered<Models.ClassWithBaseDependenciesAndInjectFields>? DiscoverFromMember(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol.ContainingType is not { } type) return null;

        // Only the first marked member (across every partial part) discovers the type.
        var first = type.GetMembers().FirstOrDefault(HasMarker);
        if (!SymbolEqualityComparer.Default.Equals(first, context.TargetSymbol)) return null;

        return Discover(type, ct);
    }

    private static Discovered<Models.ClassWithBaseDependenciesAndInjectFields>? DiscoverFromDerivedType(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, ct) is not INamedTypeSymbol type) return null;

        // A marked type is discovered from its members; this provider is only for the unmarked ones.
        if (type.GetMembers().Any(HasMarker)) return null;

        return Discover(type, ct);
    }

    private static bool HasMarker(ISymbol member)
        => member is IFieldSymbol or IPropertySymbol or IMethodSymbol
            && member.GetAttributes().Any(a => MarkerAttributeNames.Any(name => a.AttributeClass.IsAttribute(name)));

    /// <summary>
    /// The model for <paramref name="type"/>, with its diagnostics. Null when an unmarked type has nothing to forward.
    /// </summary>
    private static Discovered<Models.ClassWithBaseDependenciesAndInjectFields>? Discover(INamedTypeSymbol type, CancellationToken ct)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var pathSpec = type.GetPathSpec(ct);
        var location = type.Locations.FirstOrDefault();

        var unsupportedReason = GetUnsupportedReason(type, pathSpec);
        var members = CollectMembers(type, diagnostics, reportNotPartial: unsupportedReason is not null, ct);

        if (unsupportedReason is not null)
        {
            // An unmarked type never opted in: say nothing. CONFIG001 already named the problem on each config member.
            if (members.HasMarkers && !diagnostics.Any(d => d.RuleId == DiagnosticRules.ConfigNonPartialClass.Id))
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.InjectTypeCannotBeExtended, location, type.Name, unsupportedReason));
            return Result(null, diagnostics, members.HasMarkers);
        }

        if (HasParameterList(type, ct))
        {
            if (members.HasMarkers)
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.InjectTypeHasParameterList, location, type.Name));
            return Result(null, diagnostics, members.HasMarkers);
        }

        var (parameters, baseParameters) = ComputeParameters(type, members, depth: 0, ct);

        var hasConfigWork = members.ConfigFields.Count > 0 || members.ConnectionStringFields.Count > 0;
        var hasDependencies = parameters.Count > 0 || hasConfigWork;
        var postConstructMethodName = GetPostConstructMethod(members.PostConstructMethods, type.Name, hasDependencies, diagnostics);

        // An unmarked type is only a target when its base class has dependencies to forward.
        if (!members.HasMarkers && baseParameters.Count == 0)
            return null;

        if (parameters.Count == 0 && !hasConfigWork)
            return Result(null, diagnostics, members.HasMarkers);

        // A constructor with the same signature exists already: generating another would be CS0111.
        if (parameters.Count > 0 && type.HasMatchingConstructor(parameters.Select(p => p.MatchType).ToList()))
            return Result(null, diagnostics, members.HasMarkers);

        var model = new Models.ClassWithBaseDependenciesAndInjectFields
        {
            PathSpec = pathSpec,
            ClassName = SymbolExtensions.EscapeIdentifier(type.Name),
            IsGeneric = type.TypeParameters.Length > 0,
            IsValueType = type.IsValueType,
            ConstructorAccessibility = GetConstructorAccessibility(type, parameters),
            Parameters = parameters.Select(p => new Models.InjectField { Type = p.Type, Name = p.Name, IsNullable = p.IsOptional }).ToList(),
            BaseConstructorArguments = baseParameters.Select(p => p.Name).Distinct().ToList(),
            InjectFields = members.InjectFields.Select(f => f.Field).ToList(),
            ConfigFields = members.ConfigFields,
            ConnectionStringFields = members.ConnectionStringFields,
            OptionsFields = members.OptionsFields.Select(f => f.Field).ToList(),
            PostConstructMethodName = postConstructMethodName,
            HasExplicitIConfiguration = members.ExplicitIConfigurationName is not null,
            ExplicitIConfigurationName = members.ExplicitIConfigurationName,
        };

        return new Discovered<Models.ClassWithBaseDependenciesAndInjectFields>(model, diagnostics.ToEquatableArray());
    }

    private static Discovered<Models.ClassWithBaseDependenciesAndInjectFields>? Result(Models.ClassWithBaseDependenciesAndInjectFields? model, List<DiagnosticInfo> diagnostics, bool hasMarkers)
        => !hasMarkers && model is null && diagnostics.Count == 0
            ? null
            : new Discovered<Models.ClassWithBaseDependenciesAndInjectFields>(model, diagnostics.ToEquatableArray());

    /// <summary>Why no partial of <paramref name="type"/> can be emitted, as a message fragment; null when one can.</summary>
    private static string? GetUnsupportedReason(INamedTypeSymbol type, PathSpec? pathSpec)
    {
        if (type.TypeKind == TypeKind.Interface)
            return $"'{type.Name}' is an interface, which has no constructor";
        if (pathSpec is null)
            return $"'{type.Name}' cannot be re-declared";

        var name = pathSpec.UnsupportedTypeName ?? type.Name;
        return pathSpec.UnsupportedReason switch
        {
            EPathSpecUnsupportedReason.None => null,
            EPathSpecUnsupportedReason.TargetNotPartial => $"'{name}' is not partial",
            EPathSpecUnsupportedReason.ParentNotPartial => $"its containing type '{name}' is not partial",
            EPathSpecUnsupportedReason.FileLocal => $"'{name}' is file-local, so a generated file cannot extend it",
            _ => $"'{name}' is not a class, record or struct",
        };
    }

    /// <summary>A positional record, or a class or struct with a primary constructor.</summary>
    private static bool HasParameterList(INamedTypeSymbol type, CancellationToken ct)
        => type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .OfType<TypeDeclarationSyntax>()
            .Any(d => d.ParameterList is not null);

    #region Members

    private sealed class Members
    {
        public bool HasMarkers;
        public readonly List<(Models.InjectField Field, ITypeSymbol Type)> InjectFields = [];
        public readonly List<(OptionsField Field, ITypeSymbol Type)> OptionsFields = [];
        public readonly List<ConfigField> ConfigFields = [];
        public readonly List<ConnectionStringField> ConnectionStringFields = [];
        public readonly List<IMethodSymbol> PostConstructMethods = [];
        public string? ExplicitIConfigurationName;

        /// <summary>Whether the type gets a constructor of its own (as opposed to only forwarding its base's).</summary>
        public bool HasGeneratedMembers => InjectFields.Count > 0 || OptionsFields.Count > 0 || ConfigFields.Count > 0 || ConnectionStringFields.Count > 0;

        public bool NeedsConfiguration => (ConfigFields.Count > 0 || ConnectionStringFields.Count > 0) && ExplicitIConfigurationName is null;
    }

    /// <summary>
    /// Reads the marked members of every partial part of <paramref name="type"/>. With <paramref name="diagnostics"/>
    /// null (a base class), nothing is reported.
    /// </summary>
    private static Members CollectMembers(INamedTypeSymbol type, List<DiagnosticInfo>? diagnostics, bool reportNotPartial, CancellationToken ct)
    {
        var result = new Members();
        var usedConfigKeys = new HashSet<string>();

        foreach (var member in type.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            switch (member)
            {
                case IFieldSymbol { IsImplicitlyDeclared: false } field:
                    CollectDataMember(result, field, field.Type, isAssignable: true, type.Name, diagnostics, reportNotPartial, usedConfigKeys);
                    break;
                case IPropertySymbol { IsIndexer: false } property:
                    // A get-only (or init-only) property can be assigned in the constructor; one with a setter is not
                    // injected (INJECT008).
                    var isAssignable = property.SetMethod is null || property.SetMethod.IsInitOnly;
                    CollectDataMember(result, property, property.Type, isAssignable, type.Name, diagnostics, reportNotPartial, usedConfigKeys);
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method when method.HasAttribute(PostConstructAttributeName):
                    result.HasMarkers = true;
                    result.PostConstructMethods.Add(method);
                    break;
            }
        }

        result.ExplicitIConfigurationName = result.InjectFields
            .Where(f => IsIConfiguration(f.Type))
            .Select(f => f.Field.Name)
            .FirstOrDefault();

        return result;
    }

    private static void CollectDataMember(Members result, ISymbol member, ITypeSymbol memberType, bool isAssignable, string className,
        List<DiagnosticInfo>? diagnostics, bool reportNotPartial, HashSet<string> usedConfigKeys)
    {
        var injectAttr = member.FindAttribute(InjectAttributeName);
        var configAttr = member.FindAttribute(ConfigAttributeName);
        var connStrAttr = member.FindAttribute(ConnectionStringAttributeName);
        var optionsAttr = member.FindAttribute(OptionsAttributeName);
        if (injectAttr is null && configAttr is null && connStrAttr is null && optionsAttr is null) return;

        result.HasMarkers = true;
        var name = SymbolExtensions.EscapeIdentifier(member.Name);
        var location = member.Locations.FirstOrDefault();
        var isNullable = IsNullable(memberType);
        var hasInject = injectAttr is not null;

        void Report(DiagnosticDescriptor rule, params object?[] args) => diagnostics?.Add(DiagnosticInfo.Create(rule, location, args));

        if (hasInject && isAssignable)
        {
            result.InjectFields.Add((new Models.InjectField
            {
                Type = memberType.ToDisplayString(ParameterTypeFormat),
                Name = name,
                IsNullable = isNullable,
            }, memberType));
        }
        else if (hasInject && configAttr is null && connStrAttr is null && optionsAttr is null)
        {
            // With another marker the conflict rule below names the problem.
            Report(DiagnosticRules.InjectPropertyHasSetter, name);
        }

        if (configAttr is null && connStrAttr is null && optionsAttr is null) return;

        // Declared types keep the format the config emitters always used (no '?' on a reference type).
        var fqn = memberType.ToDisplayString(MatchTypeFormat);

        if (reportNotPartial)
        {
            Report(DiagnosticRules.ConfigNonPartialClass, className);
            return;
        }

        if (configAttr is not null)
        {
            if (hasInject) { Report(DiagnosticRules.ConfigConflictWithInject, name); return; }
            if (connStrAttr is not null) { Report(DiagnosticRules.ConfigConflictWithConnectionString, name); return; }
            if (optionsAttr is not null) { Report(DiagnosticRules.OptionsConflictWithConfig, name); return; }

            var configField = ExtractConfigField(configAttr, name, fqn, isNullable, memberType, Report, className, usedConfigKeys);
            if (configField is not null) result.ConfigFields.Add(configField);
        }
        else if (connStrAttr is not null)
        {
            if (hasInject) { Report(DiagnosticRules.ConnectionStringConflictWithInject, name); return; }

            var connStrField = ExtractConnectionStringField(connStrAttr, name, isNullable, memberType, Report);
            if (connStrField is not null) result.ConnectionStringFields.Add(connStrField);
        }
        else if (optionsAttr is not null)
        {
            if (hasInject) { Report(DiagnosticRules.OptionsConflictWithInject, name); return; }

            var optionsField = ExtractOptionsField(name, fqn, memberType, Report);
            if (optionsField is not null) result.OptionsFields.Add((optionsField, memberType));
        }
    }

    private static bool IsNullable(ITypeSymbol type)
        => type.NullableAnnotation == NullableAnnotation.Annotated
        || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsIConfiguration(ITypeSymbol type)
        => type is INamedTypeSymbol named && named.IsAttribute("Microsoft.Extensions.Configuration.IConfiguration");

    #endregion

    #region Constructor parameters

    /// <summary>One constructor parameter during discovery. Never stored in a model: it holds a symbol.</summary>
    /// <param name="Type">As written in the constructor.</param>
    /// <param name="MatchType">As <see cref="SymbolExtensions.HasMatchingConstructor"/> compares it.</param>
    /// <param name="Symbol">For the accessibility check; null for the auto-injected IConfiguration.</param>
    private sealed class Parameter(string Type, string MatchType, string Name, bool IsOptional, ITypeSymbol? Symbol)
    {
        public string Type { get; } = Type;
        public string MatchType { get; } = MatchType;
        public string Name { get; } = Name;
        public bool IsOptional { get; } = IsOptional;
        public ITypeSymbol? Symbol { get; } = Symbol;
    }

    /// <summary>
    /// The parameters of the constructor generated for <paramref name="type"/>, in emitted order, and the subset
    /// passed on to the base constructor (in the base constructor's order).
    /// </summary>
    /// <remarks>
    /// A base class in source gets its own generated constructor, which the compilation cannot see yet. Its
    /// parameters are therefore computed with this same method, so the derived class's <c>base(...)</c> call
    /// matches what the base class's constructor will be.
    /// </remarks>
    private static (List<Parameter> Parameters, List<Parameter> BaseParameters) ComputeParameters(INamedTypeSymbol type, Members members, int depth, CancellationToken ct)
    {
        var parameters = new List<Parameter>();

        if (members.NeedsConfiguration)
            parameters.Add(new Parameter(IConfigurationType, IConfigurationType, "__configuration", false, null));

        foreach (var (field, fieldType) in members.InjectFields)
            parameters.Add(new Parameter(field.Type!, fieldType.ToDisplayString(MatchTypeFormat), field.Name!, field.IsNullable, fieldType));

        foreach (var (field, fieldType) in members.OptionsFields)
            parameters.Add(new Parameter(field.Type, fieldType.ToDisplayString(MatchTypeFormat), field.Name, false, fieldType));

        var baseParameters = GetBaseParameters(type.BaseType, depth + 1, ct);
        foreach (var parameter in baseParameters)
        {
            if (!parameters.Any(p => p.Type == parameter.Type && p.Name == parameter.Name))
                parameters.Add(parameter);
        }

        // Optional parameters last (CS1737); otherwise the declaration order stays.
        var ordered = parameters.Where(p => !p.IsOptional).Concat(parameters.Where(p => p.IsOptional)).ToList();
        return (ordered, baseParameters);
    }

    /// <summary>The parameters of the constructor <paramref name="baseType"/> is constructed through.</summary>
    private static List<Parameter> GetBaseParameters(INamedTypeSymbol? baseType, int depth, CancellationToken ct)
    {
        if (baseType is null || depth > 32
            || baseType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType
            || baseType.TypeKind == TypeKind.Error)
            return [];

        var inSource = baseType.DeclaringSyntaxReferences.Length > 0;
        if (inSource)
        {
            // Every partial part counts, not just the first declaration (W4).
            var members = CollectMembers(baseType, diagnostics: null, reportNotPartial: false, ct);
            if (members.HasGeneratedMembers)
                return ComputeParameters(baseType, members, depth, ct).Parameters;
        }

        var constructor = baseType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility != Accessibility.Private && c.Parameters.Length > 0)
            // A record's copy constructor is not a way to construct a derived instance.
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, baseType)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (constructor is not null)
        {
            return constructor.Parameters
                .Select(p => new Parameter(
                    p.Type.ToDisplayString(ParameterTypeFormat),
                    p.Type.ToDisplayString(MatchTypeFormat),
                    SymbolExtensions.EscapeIdentifier(p.Name),
                    false,
                    p.Type))
                .ToList();
        }

        // A base in source without dependencies of its own gets a constructor forwarding its own base's.
        return inSource ? GetBaseParameters(baseType.BaseType, depth + 1, ct) : [];
    }

    /// <summary>
    /// <c>public</c>, unless a parameter type is less accessible than the type itself: a public constructor
    /// taking an internal type is CS0051. Stays <c>public</c> otherwise, because the container only calls public
    /// constructors.
    /// </summary>
    private static string GetConstructorAccessibility(INamedTypeSymbol type, List<Parameter> parameters)
    {
        var typeAccessibility = EffectiveAccessibility(type);
        var parameterAccessibility = parameters
            .Where(p => p.Symbol is not null)
            .Aggregate(Accessibility.Public, (acc, p) => Min(acc, EffectiveAccessibility(p.Symbol!)));

        var least = Min(typeAccessibility, parameterAccessibility);
        if (least == typeAccessibility) return "public";

        // A struct cannot declare a protected member (CS0666), and a sealed class warns about one (CS0628).
        var noProtected = type.IsValueType || type.IsSealed;
        return least switch
        {
            Accessibility.Internal => "internal",
            Accessibility.Protected => noProtected ? "private" : "protected",
            Accessibility.ProtectedOrInternal => noProtected ? "internal" : "protected internal",
            Accessibility.ProtectedAndInternal => noProtected ? "private" : "private protected",
            Accessibility.Private => "private",
            _ => "public",
        };
    }

    private static Accessibility EffectiveAccessibility(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return EffectiveAccessibility(array.ElementType);
            case IPointerTypeSymbol pointer:
                return EffectiveAccessibility(pointer.PointedAtType);
            case INamedTypeSymbol named:
                var result = Accessibility.Public;
                for (var current = named; current is not null; current = current.ContainingType)
                    result = Min(result, current.DeclaredAccessibility);
                foreach (var argument in named.TypeArguments)
                    result = Min(result, EffectiveAccessibility(argument));
                return result;
            default:
                // Type parameters, dynamic: no restriction of their own.
                return Accessibility.Public;
        }
    }

    /// <summary>The accessibility whose domain is the intersection of both.</summary>
    private static Accessibility Min(Accessibility a, Accessibility b)
    {
        if (a == Accessibility.NotApplicable) return b;
        if (b == Accessibility.NotApplicable) return a;
        if (a == b) return a;
        if (a == Accessibility.Public) return b;
        if (b == Accessibility.Public) return a;
        if (a == Accessibility.Private || b == Accessibility.Private) return Accessibility.Private;
        if (a == Accessibility.ProtectedOrInternal) return b;
        if (b == Accessibility.ProtectedOrInternal) return a;
        // Two different ones of Protected, Internal and ProtectedAndInternal.
        return Accessibility.ProtectedAndInternal;
    }

    #endregion

    private static string? GetPostConstructMethod(List<IMethodSymbol> methods, string className, bool hasDependencies, List<DiagnosticInfo> diagnostics)
    {
        var valid = new List<IMethodSymbol>();

        foreach (var method in methods)
        {
            var location = method.Locations.FirstOrDefault();

            if (method.IsStatic)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.PostConstructCannotBeStatic, location, method.Name));
                continue;
            }

            if (method.Parameters.Length > 0)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.PostConstructMustBeParameterless, location, method.Name));
                continue;
            }

            // A Task would be dropped by the constructor: fire-and-forget, and CS4014 in generated code (P5).
            if (!method.ReturnsVoid)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.PostConstructMustReturnVoid, location, method.Name,
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                continue;
            }

            valid.Add(method);
        }

        if (valid.Count > 1)
        {
            foreach (var method in valid)
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.OnlyOnePostConstructAllowed, method.Locations.FirstOrDefault(), className));
            return null;
        }

        if (valid.Count == 1 && !hasDependencies)
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.PostConstructRequiresInject, valid[0].Locations.FirstOrDefault(), valid[0].Name, className));

        return valid.Count == 1 ? SymbolExtensions.EscapeIdentifier(valid[0].Name) : null;
    }

    #region Config members

    /// <summary>
    /// Extracts ConfigField from a [Config] attribute. Arguments are read from the bound attribute, so a
    /// <c>const</c>, a concatenation or a <c>nameof</c> key works (P3).
    /// </summary>
    private static ConfigField? ExtractConfigField(
        AttributeData configAttr,
        string fieldName,
        string fqn,
        bool isNullableFromType,
        ITypeSymbol typeSymbol,
        Action<DiagnosticDescriptor, object?[]> report,
        string className,
        HashSet<string> usedConfigKeys)
    {
        var key = configAttr.GetArgument<string?>("key", null);
        var hasDefaultValue = configAttr.TryGetArgument<object?>("DefaultValue", out var defaultValue);

        // Only constants belong in the model: a typeof or an array argument is not a default value we can emit.
        if (defaultValue is ISymbol or ImmutableArray<TypedConstant>)
            defaultValue = null;

        // Validate key
        if (string.IsNullOrWhiteSpace(key))
        {
            report(DiagnosticRules.ConfigEmptyKey, []);
            return null;
        }

        // Check for duplicate keys
        if (!usedConfigKeys.Add(key!))
            report(DiagnosticRules.ConfigDuplicateKey, [key, className]);

        // Determine type category
        var (typeCategory, isNullableFromClassify, isEnum, isComplexType, isCollection, elementType) = ClassifyType(typeSymbol);

        if (typeCategory == ConfigFieldTypeCategory.Unsupported)
        {
            report(DiagnosticRules.ConfigUnsupportedType, [fqn]);
            return null;
        }

        if (hasDefaultValue && configAttr.TryGetArgument<TypedConstant>("DefaultValue", out var defaultConstant)
            && !IsCompatibleDefault(defaultConstant, typeSymbol, typeCategory))
        {
            var defaultType = defaultConstant.IsNull ? "null" : defaultConstant.Type?.ToDisplayString() ?? "?";
            report(DiagnosticRules.ConfigDefaultValueTypeMismatch, [defaultType, typeSymbol.ToDisplayString()]);
            return null;
        }

        return new ConfigField
        {
            Key = key!,
            Type = fqn,
            Name = fieldName,
            DefaultValue = defaultValue,
            HasDefaultValue = hasDefaultValue,
            IsNullable = isNullableFromType || isNullableFromClassify,
            IsEnum = isEnum,
            IsComplexType = isComplexType,
            IsCollection = isCollection,
            ElementOrUnderlyingType = elementType,
            TypeCategory = typeCategory
        };
    }

    /// <summary>
    /// Extracts ConnectionStringField from a [ConnectionString] attribute.
    /// </summary>
    private static ConnectionStringField? ExtractConnectionStringField(
        AttributeData connStrAttr,
        string fieldName,
        bool isNullable,
        ITypeSymbol typeSymbol,
        Action<DiagnosticDescriptor, object?[]> report)
    {
        var name = connStrAttr.GetArgument<string?>("name", null);

        if (string.IsNullOrWhiteSpace(name))
        {
            report(DiagnosticRules.ConnectionStringEmptyName, []);
            return null;
        }

        if (typeSymbol.SpecialType != SpecialType.System_String)
        {
            report(DiagnosticRules.ConnectionStringInvalidType, [fieldName, typeSymbol.ToDisplayString()]);
            return null;
        }

        return new ConnectionStringField
        {
            Name = name!,
            FieldName = fieldName,
            IsNullable = isNullable
        };
    }

    /// <summary>
    /// Extracts OptionsField from an [Options] attribute.
    /// </summary>
    /// <remarks>
    /// The attribute's section is documentation only: the options are bound where they are registered
    /// (<c>services.AddOptions&lt;T&gt;().BindConfiguration("Section")</c>), so it is not read here.
    /// </remarks>
    private static OptionsField? ExtractOptionsField(
        string fieldName,
        string fqn,
        ITypeSymbol typeSymbol,
        Action<DiagnosticDescriptor, object?[]> report)
    {
        // Validate type is IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>
        if (typeSymbol is not INamedTypeSymbol namedType || !namedType.IsGenericType)
        {
            report(DiagnosticRules.OptionsInvalidType, [fieldName, fqn]);
            return null;
        }

        var typeName = namedType.OriginalDefinition.ToDisplayString();
        OptionsFieldKind kind;

        if (typeName == "Microsoft.Extensions.Options.IOptions<T>" ||
            typeName.EndsWith(".IOptions<T>") ||
            namedType.Name == "IOptions")
        {
            kind = OptionsFieldKind.Options;
        }
        else if (typeName == "Microsoft.Extensions.Options.IOptionsSnapshot<T>" ||
                 typeName.EndsWith(".IOptionsSnapshot<T>") ||
                 namedType.Name == "IOptionsSnapshot")
        {
            kind = OptionsFieldKind.OptionsSnapshot;
        }
        else if (typeName == "Microsoft.Extensions.Options.IOptionsMonitor<T>" ||
                 typeName.EndsWith(".IOptionsMonitor<T>") ||
                 namedType.Name == "IOptionsMonitor")
        {
            kind = OptionsFieldKind.OptionsMonitor;
        }
        else
        {
            report(DiagnosticRules.OptionsInvalidType, [fieldName, fqn]);
            return null;
        }

        return new OptionsField
        {
            Type = typeSymbol.ToDisplayString(ParameterTypeFormat),
            Name = fieldName,
            OptionsType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Kind = kind
        };
    }

    /// <summary>
    /// Whether the producer can emit <paramref name="constant"/> as the fallback of a field of
    /// <paramref name="fieldType"/>, so that it compiles and is the value written (CONFIG005).
    /// </summary>
    private static bool IsCompatibleDefault(TypedConstant constant, ITypeSymbol fieldType, ConfigFieldTypeCategory category)
    {
        // A typeof or an array is never emitted.
        if (constant.Kind is TypedConstantKind.Type or TypedConstantKind.Array or TypedConstantKind.Error) return false;

        var underlying = fieldType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : fieldType;

        // Null fits a Nullable<T>, and a reference type unless the nullable context says it is not nullable.
        if (constant.IsNull)
            return fieldType.IsValueType
                ? !SymbolEqualityComparer.Default.Equals(underlying, fieldType)
                : fieldType.NullableAnnotation != NullableAnnotation.NotAnnotated;

        var value = constant.Value;
        switch (category)
        {
            case ConfigFieldTypeCategory.String:
                return value is string;
            case ConfigFieldTypeCategory.Boolean:
                return value is bool;
            case ConfigFieldTypeCategory.Char:
                return value is char;
            case ConfigFieldTypeCategory.Uri:
                return value is string;
            case ConfigFieldTypeCategory.Enum:
                if (constant.Kind == TypedConstantKind.Enum)
                    return SymbolEqualityComparer.Default.Equals(constant.Type, underlying);
                if (value is string name)
                    return underlying.GetMembers(name).Any(m => m is IFieldSymbol { HasConstantValue: true });
                return IsIntegral(value);
            case ConfigFieldTypeCategory.Numeric:
                return constant.Kind == TypedConstantKind.Primitive && FitsNumeric(value!, underlying.SpecialType);
            default:
                // DateTime, TimeSpan, DateOnly, TimeOnly, Guid, POCOs and collections have no constant form.
                return false;
        }
    }

    private static bool IsIntegral(object? value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong;

    /// <summary>
    /// The producer writes the value as a literal (an integer without suffix, a float, double or decimal with its
    /// suffix, a char in quotes), so this is the C# rule for that literal: an integer converts when it fits, a char
    /// widens to ushort and up, a float to double; a floating-point or decimal literal never narrows to an integer.
    /// </summary>
    private static bool FitsNumeric(object value, SpecialType target)
    {
        if (value is char c)
            return target is SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

        if (IsIntegral(value))
        {
            if (target is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal) return true;

            // Every integral value fits in decimal, so compare there.
            var number = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            return target switch
            {
                SpecialType.System_SByte => number >= sbyte.MinValue && number <= sbyte.MaxValue,
                SpecialType.System_Byte => number >= byte.MinValue && number <= byte.MaxValue,
                SpecialType.System_Int16 => number >= short.MinValue && number <= short.MaxValue,
                SpecialType.System_UInt16 => number >= ushort.MinValue && number <= ushort.MaxValue,
                SpecialType.System_Int32 => number >= int.MinValue && number <= int.MaxValue,
                SpecialType.System_UInt32 => number >= uint.MinValue && number <= uint.MaxValue,
                SpecialType.System_Int64 => number >= long.MinValue && number <= long.MaxValue,
                SpecialType.System_UInt64 => number >= ulong.MinValue && number <= ulong.MaxValue,
                _ => false,
            };
        }

        return value switch
        {
            float => target is SpecialType.System_Single or SpecialType.System_Double,
            double => target is SpecialType.System_Double,
            _ => false,
        };
    }

    #endregion

    /// <summary>
    /// Classifies a type symbol into a ConfigFieldTypeCategory.
    /// </summary>
    private static (ConfigFieldTypeCategory Category, bool IsNullable, bool IsEnum, bool IsComplexType, bool IsCollection, string? ElementType) ClassifyType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return (ConfigFieldTypeCategory.Unsupported, false, false, false, false, null);

        var isNullable = false;
        var underlyingType = typeSymbol;

        // Handle nullable value types (Nullable<T>)
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            isNullable = true;
            underlyingType = namedType.TypeArguments[0];
        }

        // Handle nullable reference types
        if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            isNullable = true;
        }

        // Check for string
        if (underlyingType.SpecialType == SpecialType.System_String)
            return (ConfigFieldTypeCategory.String, isNullable, false, false, false, null);

        // Check for bool
        if (underlyingType.SpecialType == SpecialType.System_Boolean)
            return (ConfigFieldTypeCategory.Boolean, isNullable, false, false, false, null);

        // Check for char
        if (underlyingType.SpecialType == SpecialType.System_Char)
            return (ConfigFieldTypeCategory.Char, isNullable, false, false, false, null);

        // Check for numeric types
        if (underlyingType.SpecialType is
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Decimal)
        {
            return (ConfigFieldTypeCategory.Numeric, isNullable, false, false, false, null);
        }

        // Check for enum
        if (underlyingType.TypeKind == TypeKind.Enum)
        {
            var enumFqn = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (ConfigFieldTypeCategory.Enum, isNullable, true, false, false, enumFqn);
        }

        // Check for DateTime types
        var fullName = underlyingType.ToDisplayString();
        if (fullName == "System.DateTime" || fullName == "global::System.DateTime")
            return (ConfigFieldTypeCategory.DateTime, isNullable, false, false, false, null);

        if (fullName == "System.DateTimeOffset" || fullName == "global::System.DateTimeOffset")
            return (ConfigFieldTypeCategory.DateTime, isNullable, false, false, false, null);

        if (fullName == "System.TimeSpan" || fullName == "global::System.TimeSpan")
            return (ConfigFieldTypeCategory.TimeSpan, isNullable, false, false, false, null);

        if (fullName == "System.DateOnly" || fullName == "global::System.DateOnly")
            return (ConfigFieldTypeCategory.DateOnly, isNullable, false, false, false, null);

        if (fullName == "System.TimeOnly" || fullName == "global::System.TimeOnly")
            return (ConfigFieldTypeCategory.TimeOnly, isNullable, false, false, false, null);

        if (fullName == "System.Guid" || fullName == "global::System.Guid")
            return (ConfigFieldTypeCategory.Guid, isNullable, false, false, false, null);

        if (fullName == "System.Uri" || fullName == "global::System.Uri")
            return (ConfigFieldTypeCategory.Uri, isNullable, false, false, false, null);

        // Check for arrays
        if (underlyingType is IArrayTypeSymbol arrayType)
        {
            var elementFqn = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (ConfigFieldTypeCategory.Collection, isNullable, false, false, true, elementFqn);
        }

        // Check for List<T>, IEnumerable<T>, IList<T>, ICollection<T>
        if (underlyingType is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            var genericDef = genericType.OriginalDefinition.ToDisplayString();
            if (genericDef.StartsWith("System.Collections.Generic.List<") ||
                genericDef.StartsWith("System.Collections.Generic.IList<") ||
                genericDef.StartsWith("System.Collections.Generic.IEnumerable<") ||
                genericDef.StartsWith("System.Collections.Generic.ICollection<") ||
                genericType.Name == "List" || genericType.Name == "IList" ||
                genericType.Name == "IEnumerable" || genericType.Name == "ICollection")
            {
                var elementFqn = genericType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (ConfigFieldTypeCategory.Collection, isNullable, false, false, true, elementFqn);
            }
        }

        // Treat as complex type (POCO) - use GetSection().Get<T>()
        if (underlyingType.TypeKind == TypeKind.Class || underlyingType.TypeKind == TypeKind.Struct)
        {
            return (ConfigFieldTypeCategory.Complex, isNullable, false, true, false, null);
        }

        return (ConfigFieldTypeCategory.Unsupported, isNullable, false, false, false, null);
    }
}
