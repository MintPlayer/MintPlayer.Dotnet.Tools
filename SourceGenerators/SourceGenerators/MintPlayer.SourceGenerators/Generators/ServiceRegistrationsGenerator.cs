using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.Text;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class ServiceRegistrationsGenerator : IncrementalGenerator
{
    // Metadata names, never typeof(X).FullName or the simple name: a same-named attribute in another namespace must
    // not match, and the simple-name comparison this replaces did match it.
    internal const string RegisterAttributeName = "MintPlayer.SourceGenerators.Attributes.RegisterAttribute";
    internal const string RegisterFactoryAttributeName = "MintPlayer.SourceGenerators.Attributes.RegisterFactoryAttribute";
    internal const string ConfigurationAttributeName = "MintPlayer.SourceGenerators.Attributes.ServiceRegistrationConfigurationAttribute";
    private const string ServiceLifetimeName = "Microsoft.Extensions.DependencyInjection.ServiceLifetime";
    private const string ServiceProviderName = "System.IServiceProvider";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        // One callback per attributed declaration, with only that declaration's attributes. The syntax provider this
        // replaces visited every class and read the symbol's attributes, so a partial class split over two files was
        // registered twice (W5), and records were never visited at all (K3).
        var classRegistrations = context.SyntaxProvider.ForAttribute(
            RegisterAttributeName,
            static (node, ct) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
            static (ctx, ct) => DiscoverClass(ctx, ct));

        var classRegistrationsProvider = classRegistrations
            .Models()
            .SelectMany(static (set, ct) => set.Registrations.AsImmutableArray())
            .Collect();

        // Assembly-level [Register] attributes (for third-party types). A CompilationProvider.Select re-runs on every
        // edit, so its result must compare by value for anything downstream to be served from cache.
        var assemblyRegistrations = context.CompilationProvider
            .Select(static (compilation, ct) => DiscoverAssembly(compilation, ct));

        var knowsDependencyInjectionAbstractionsProvider = context.CompilationProvider
            .Select((compilation, ct) => compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null);

        // Extract assembly configuration (assembly name and ServiceRegistrationConfigurationAttribute)
        var assemblyConfigProvider = context.CompilationProvider
            .Select((compilation, ct) =>
            {
                var configAttr = compilation.Assembly.FindAttribute(ConfigurationAttributeName);

                // GetArgument, not a cast of the raw value: mid-typing (`DefaultAccessibility = )`) the value is null,
                // and the cast this replaces threw a NullReferenceException that took every registration down (C2).
                var defaultAccessibility = configAttr.GetArgument(nameof(ServiceRegistrationConfigurationAttribute.DefaultAccessibility), EGeneratedAccessibility.Unspecified);

                return new AssemblyRegistrationConfig
                {
                    AssemblyName = compilation.Assembly.Name,
                    DefaultMethodName = configAttr.GetArgument<string?>(nameof(ServiceRegistrationConfigurationAttribute.DefaultMethodName), null),
                    DefaultAccessibility = Enum.IsDefined(typeof(EGeneratedAccessibility), defaultAccessibility) ? defaultAccessibility : EGeneratedAccessibility.Unspecified,
                };
            });

        // Combine class-level and assembly-level registrations. The concatenation is a new
        // collection on every run, so it is an EquatableArray to compare by value.
        var allRegistrationsProvider = classRegistrationsProvider
            .Combine(assemblyRegistrations)
            .Select(static (combined, ct) => combined.Left.Concat(combined.Right.Model?.Registrations ?? EquatableArray<ServiceRegistration>.Empty).ToEquatableArray());

        var registerAttributeSourceProvider = allRegistrationsProvider
            .Join(knowsDependencyInjectionAbstractionsProvider)
            .Join(settingsProvider)
            .Join(assemblyConfigProvider)
            .Select(static Producer (providers, ct) => new RegistrationsProducer(
                providers.Item1.AsEnumerable(),
                providers.Item2,
                providers.Item3.RootNamespace!,
                providers.Item4
            ));

        context.ProduceCode(registerAttributeSourceProvider);
        context.ReportDiagnostics(
            classRegistrations.Diagnostics(),
            assemblyRegistrations.SelectMany(static (d, ct) => d.Diagnostics.AsImmutableArray()));
    }

    private static Discovered<RegistrationSet>? DiscoverClass(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        // A struct is n.a.: [Register] is AttributeUsage(Class), so the user already has CS0592.
        if (ctx.TargetSymbol is not INamedTypeSymbol type || type.TypeKind != TypeKind.Class)
            return null;

        var compilation = ctx.SemanticModel.Compilation;
        var registrations = new List<ServiceRegistration>();
        var diagnostics = new List<DiagnosticInfo>();

        // Read once per declaration, not per attribute.
        var factories = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.IsStatic && m.HasAttribute(RegisterFactoryAttributeName))
            .Select(m => (m.Name, ReturnType: m.ReturnType as INamedTypeSymbol, Shape: ClassifyFactory(m)))
            .Where(m => m.ReturnType is not null && m.Shape != EFactoryShape.Unsupported)
            .ToArray();

        foreach (var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            // Only a registration that reports a diagnostic carries its location. A line-based location changes
            // whenever code above the class grows, so a clean registration holding one would re-run the output step
            // for every edit higher up in the file.
            Location? AttributeLocation() => attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? type.Locations.FirstOrDefault();

            var formalParamCount = attr.AttributeConstructor?.Parameters.Length ?? 0; // includes optional params

            // Error: 5-param constructor (assembly-level) used on a class
            if (formalParamCount == 5)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.RegisterAttributeClassRequiresLifetime, AttributeLocation()));
                continue;
            }

            // Constructor forms: 3 params (no service type), 4 params (with service type). Anything else is an
            // attribute that did not bind (mid-typing): the user already has a compile error.
            if (formalParamCount is not (3 or 4))
                continue;

            if (!TryReadLifetime(attr, AttributeLocation, diagnostics, out var lifetime))
                continue;

            var methodNameHint = attr.GetArgument<string?>("methodNameHint", null);
            var accessibility = attr.GetArgument("accessibility", EGeneratedAccessibility.Unspecified);

            INamedTypeSymbol serviceType;
            if (formalParamCount == 3)
            {
                // (ServiceLifetime lifetime, string methodNameHint = default, GeneratedAccessibility accessibility = default)
                serviceType = type;
            }
            else
            {
                // (Type serviceType, ServiceLifetime lifetime, string methodNameHint = default, GeneratedAccessibility accessibility = default)
                if (!attr.TryGetArgument<INamedTypeSymbol>("serviceType", out var requested) || requested is null)
                    continue;

                if (requested.IsUnboundGenericType)
                {
                    // Find the constructed form the class actually implements or extends: IMap<TKey, TValue> for
                    // `Map<TValue, TKey> : IMap<TKey, TValue>`, or IMap<string, TValue> for `StringMap<TValue> :
                    // IMap<string, TValue>`. The service type is built from those type arguments (P10), never from the
                    // class's own type-parameter list.
                    var definition = requested.OriginalDefinition;
                    var matches = type.AllInterfaces
                        .Concat(BaseTypes(type))
                        .Where(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, definition))
                        .ToArray();

                    // Not implemented at all: skipped, as before; registering it would not compile.
                    if (matches.Length == 0)
                        continue;

                    if (matches.Length > 1)
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            DiagnosticRules.RegisterServiceTypeNotExpressible, AttributeLocation(),
                            type.Name, definition.ToDisplayString(),
                            $"it implements {string.Join(" and ", matches.Select(m => m.ToDisplayString()))}; register each one with an explicit closed type"));
                        continue;
                    }

                    serviceType = matches[0];
                }
                else
                {
                    // Non-generic service type: it must be one of the class's interfaces or base types. One it does
                    // not implement is skipped, as before; registering it would not compile.
                    var implementsInterface = type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, requested));
                    var extendsBaseClass = BaseTypes(type).Any(b => SymbolEqualityComparer.Default.Equals(b, requested));
                    if (!implementsInterface && !extendsBaseClass)
                        continue;

                    serviceType = requested;
                }
            }

            // The generated method lives in another class, so a private, protected or file-local type cannot be named
            // there: CS0122 / CS0246 in generated code (W6, P11). Report it instead.
            if ((FindInaccessible(type) ?? FindInaccessible(serviceType)) is { } inaccessible)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticRules.RegisterTypeNotAccessible, AttributeLocation(),
                    inaccessible.ToDisplayString(), DescribeAccessibility(inaccessible)));
                continue;
            }

            // Both types are rendered with the class's own type parameters as positional placeholders, so that
            // registrations of the same arity can share one generic method whatever their type parameters are called
            // (P9). A type parameter of a containing type cannot be expressed in the generated method at all.
            var ownTypeParameters = type.TypeParameters;
            string? foreign = null;
            var implementationTemplate = Template(type, ownTypeParameters, ref foreign);
            var serviceTemplate = Template(serviceType, ownTypeParameters, ref foreign);
            var constraintTemplates = ownTypeParameters
                .Select(tp => ConstraintTemplate(tp, ownTypeParameters, ref foreign))
                .Where(c => !string.IsNullOrEmpty(c))
                .ToArray();

            if (foreign is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticRules.RegisterServiceTypeNotExpressible, AttributeLocation(),
                    type.Name, serviceType.ToDisplayString(),
                    $"it uses the type parameter '{foreign}' of a containing type"));
                continue;
            }

            if (ownTypeParameters.Length > 0)
            {
                registrations.Add(new ServiceRegistration
                {
                    Lifetime = lifetime,
                    MethodNameHint = methodNameHint,
                    Accessibility = accessibility,
                    FactoryExpressions = [], // Factories not supported for open generics (yet)
                    IsGeneric = true,
                    GenericInfo = new GenericTypeInfo
                    {
                        TypeParameterNames = ownTypeParameters.Select(tp => SymbolExtensions.EscapeIdentifier(tp.Name)).ToArray(),
                        ConstraintClauses = constraintTemplates,
                        GenericServiceTypeName = serviceTemplate,
                        GenericImplementationTypeName = implementationTemplate,
                        ImplementationSimpleName = type.Name,
                    },
                });
                continue;
            }

            var selfRegistration = SymbolEqualityComparer.Default.Equals(serviceType, type);
            registrations.Add(new ServiceRegistration
            {
                // null means self-registration
                ServiceTypeName = selfRegistration ? null : serviceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ImplementationTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Lifetime = lifetime,
                MethodNameHint = methodNameHint,
                Accessibility = accessibility,
                FactoryExpressions = factories
                    .Where(f => IsUsableFactoryReturn(compilation, f.ReturnType!, serviceType))
                    .Select(f => BuildFactoryExpression(type, f.Name, f.Shape))
                    .ToArray(),
            });
        }

        return new Discovered<RegistrationSet>(
            registrations.Count == 0 ? null : new RegistrationSet { Registrations = registrations.ToEquatableArray() },
            diagnostics.ToEquatableArray());
    }

    private static Discovered<RegistrationSet> DiscoverAssembly(Compilation compilation, CancellationToken ct)
    {
        var registrations = new List<ServiceRegistration>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var attr in compilation.Assembly.FindAttributes(RegisterAttributeName))
        {
            ct.ThrowIfCancellationRequested();

            // As for the class-level provider: only an erroneous registration keeps its location.
            Location? AttributeLocation() => attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation();

            var formalParamCount = attr.AttributeConstructor?.Parameters.Length ?? 0;

            // Error: 3-param constructor (ServiceLifetime first) used on assembly
            // Pattern 1 is class-only: [Register(ServiceLifetime.Scoped)]
            if (formalParamCount == 3)
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.RegisterAttributeAssemblyRequiresType, AttributeLocation()));
                continue;
            }

            if (formalParamCount is not (4 or 5))
                continue;

            if (!TryReadLifetime(attr, AttributeLocation, diagnostics, out var lifetime))
                continue;

            var methodNameHint = attr.GetArgument<string?>("methodNameHint", null);
            var accessibility = attr.GetArgument("accessibility", EGeneratedAccessibility.Unspecified);

            if (formalParamCount == 4)
            {
                // Pattern 3: Assembly-level self-registration
                // [assembly: Register(typeof(Implementation), ServiceLifetime.Scoped)]
                // 4 formal parameters, the class-level (Type serviceType, ...) overload: the type is the implementation.
                if (!attr.TryGetArgument<INamedTypeSymbol>("serviceType", out var implementationTypeSymbol) || implementationTypeSymbol is null)
                    continue;

                registrations.Add(new ServiceRegistration
                {
                    ServiceTypeName = null, // null means self-registration
                    ImplementationTypeName = implementationTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Lifetime = lifetime,
                    MethodNameHint = methodNameHint,
                    Accessibility = accessibility,
                    FactoryExpressions = [], // No factories for assembly-level registrations
                });
            }
            else
            {
                // Pattern 4: Assembly-level interface + implementation registration
                // [assembly: Register(typeof(IService), typeof(Implementation), ServiceLifetime.Scoped)]
                if (!attr.TryGetArgument<INamedTypeSymbol>("serviceType", out var serviceTypeSymbol) || serviceTypeSymbol is null)
                    continue;
                if (!attr.TryGetArgument<INamedTypeSymbol>("implementationType", out var implementationTypeSymbol) || implementationTypeSymbol is null)
                    continue;

                registrations.Add(new ServiceRegistration
                {
                    ServiceTypeName = serviceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ImplementationTypeName = implementationTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Lifetime = lifetime,
                    MethodNameHint = methodNameHint,
                    Accessibility = accessibility,
                    FactoryExpressions = [], // No factories for assembly-level registrations
                });
            }
        }

        return new Discovered<RegistrationSet>(
            new RegistrationSet { Registrations = registrations.ToEquatableArray() },
            diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Reads the <c>lifetime</c> argument. False, with nothing reported, for an argument that did not bind (the user
    /// already has a compile error); false with REGISTER004 for a value that is not a defined
    /// <see cref="ServiceLifetime"/>, such as <c>(ServiceLifetime)7</c>, which used to crash the producer (C3).
    /// </summary>
    private static bool TryReadLifetime(AttributeData attr, Func<Location?> location, List<DiagnosticInfo> diagnostics, out ServiceLifetime lifetime)
    {
        lifetime = default;
        if (!attr.TryGetArgument<TypedConstant>("lifetime", out var constant)
            || constant.Kind == TypedConstantKind.Error
            || constant.Type is not INamedTypeSymbol lifetimeType
            || !lifetimeType.IsAttribute(ServiceLifetimeName)
            || constant.Value is not int raw)
            return false;

        lifetime = (ServiceLifetime)raw;
        if (lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped or ServiceLifetime.Transient)
            return true;

        diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.RegisterLifetimeUndefined, location(), raw));
        return false;
    }

    /// <summary>The base types of <paramref name="type"/>, nearest first.</summary>
    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    /// <summary>
    /// The first type in <paramref name="type"/> (the type itself, a containing type, or a type argument) that a
    /// method in another class of the same assembly cannot name: private, protected, private protected or
    /// <c>file</c>-local. <c>protected internal</c> is accessible within the assembly, so it is fine.
    /// </summary>
    private static INamedTypeSymbol? FindInaccessible(ITypeSymbol type)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (current.IsFileLocal || current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
                        return current;
                }
                foreach (var argument in named.TypeArguments)
                {
                    if (FindInaccessible(argument) is { } inaccessible)
                        return inaccessible;
                }
                return null;
            case IArrayTypeSymbol array:
                return FindInaccessible(array.ElementType);
            default:
                return null;
        }
    }

    private static string DescribeAccessibility(INamedTypeSymbol type) => type switch
    {
        { IsFileLocal: true } => "file-local",
        { DeclaredAccessibility: Accessibility.Private } => "private",
        { DeclaredAccessibility: Accessibility.Protected } => "protected",
        { DeclaredAccessibility: Accessibility.ProtectedAndInternal } => "private protected",
        _ => "not accessible",
    };

    /// <summary>
    /// <paramref name="type"/> as fully qualified C#, with every type parameter of <paramref name="own"/> replaced by its
    /// position as a <see cref="string.Format(string, object[])"/> placeholder: <c>global::N.IMap&lt;{1}, {0}&gt;</c>.
    /// </summary>
    /// <param name="foreign">Set to the first type parameter that is not in <paramref name="own"/>, if any.</param>
    private static string Template(ITypeSymbol type, IReadOnlyList<ITypeParameterSymbol> own, ref string? foreign)
    {
        var builder = new StringBuilder();
        foreach (var part in type.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            if (part.Symbol is ITypeParameterSymbol typeParameter)
            {
                var index = IndexOf(own, typeParameter);
                if (index >= 0)
                {
                    builder.Append('{').Append(index).Append('}');
                    continue;
                }
                foreign ??= typeParameter.Name;
            }

            builder.Append(part.ToString().Replace("{", "{{").Replace("}", "}}"));
        }
        return builder.ToString();
    }

    private static int IndexOf(IReadOnlyList<ITypeParameterSymbol> typeParameters, ITypeParameterSymbol typeParameter)
    {
        for (var i = 0; i < typeParameters.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(typeParameters[i], typeParameter))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// True when a factory returning <paramref name="returnType"/> can supply
    /// <paramref name="registeredType"/>.
    /// </summary>
    /// <remarks>
    /// Assignability, not identity. Requiring an exact match meant a factory returning the
    /// concrete implementation was silently ignored when registering against an interface, even
    /// though <c>AddScoped&lt;IGreeter&gt;(Greeter.Create)</c> compiles perfectly well — the
    /// registration still happened, just without the factory, so the container constructed the
    /// service itself and the factory never ran.
    /// </remarks>
    private static bool IsUsableFactoryReturn(Compilation compilation, ITypeSymbol returnType, INamedTypeSymbol registeredType)
    {
        if (SymbolEqualityComparer.Default.Equals(returnType, registeredType))
            return true;

        // Reference conversion only — NOT HasImplicitConversion. The factory is emitted as a
        // method group bound to Func<IServiceProvider, TRegistered>, and a method group conversion
        // permits identity and reference covariance and nothing else. HasImplicitConversion also
        // admits user-defined operators and boxing, so a factory returning some Wrapper with an
        // `implicit operator Greeter` passed the old check and emitted a method group the
        // consumer's compiler rejects with CS1503.
        var conversion = compilation.ClassifyCommonConversion(returnType, registeredType);
        return conversion is { Exists: true, IsImplicit: true, IsReference: true };
    }

    /// <summary>How a <c>[RegisterFactory]</c> method can be handed to the DI container.</summary>
    private enum EFactoryShape
    {
        /// <summary>Wrong signature entirely — skipped rather than emitted as broken code.</summary>
        Unsupported,
        /// <summary>No parameters; has to be wrapped in a lambda.</summary>
        Parameterless,
        /// <summary>Takes the provider; can be passed as a method group.</summary>
        AcceptsServiceProvider,
    }

    /// <summary>
    /// Whether a factory can be used at all, and if so how it has to be emitted.
    /// </summary>
    /// <remarks>
    /// The DI overload takes <c>Func&lt;IServiceProvider, T&gt;</c>, so only two signatures work:
    /// no parameters (wrap in a lambda) or exactly one <see cref="IServiceProvider"/> (method
    /// group). Anything else — <c>Create(string name)</c>, <c>Create(IServiceProvider, ILogger)</c>
    /// — used to be emitted as a bare method group and failed with CS1503 in the CONSUMER's build,
    /// which is the failure this whole area exists to prevent. Such a factory is now skipped:
    /// the registration still happens, just without it.
    /// </remarks>
    private static EFactoryShape ClassifyFactory(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return EFactoryShape.Parameterless;

        if (method.Parameters.Length == 1 && method.Parameters[0].Type is INamedTypeSymbol parameterType && parameterType.IsAttribute(ServiceProviderName))
            return EFactoryShape.AcceptsServiceProvider;

        return EFactoryShape.Unsupported;
    }

    /// <summary>
    /// The argument expression for a factory, as it will appear inside <c>AddScoped&lt;T&gt;(...)</c>.
    /// </summary>
    /// <remarks>
    /// The DI overload takes <c>Func&lt;IServiceProvider, T&gt;</c>. A factory that accepts the
    /// provider can be passed as a method group; a parameterless one cannot, and passing it
    /// directly produced CS1503 — "cannot convert from 'method group'" — in the CONSUMER's build,
    /// pointing at generated code they never wrote. Wrapping it in a lambda keeps the
    /// parameterless form working, which is the shape most people write first.
    /// </remarks>
    private static string BuildFactoryExpression(INamedTypeSymbol implementationType, string factoryName, EFactoryShape shape)
    {
        var qualified = $"{implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{SymbolExtensions.EscapeIdentifier(factoryName)}";
        return shape == EFactoryShape.Parameterless ? $"sp => {qualified}()" : qualified;
    }

    /// <summary>
    /// The constraint clause for a type parameter, as a template over <paramref name="own"/> (see <see cref="Template"/>),
    /// or an empty string when it has none.
    /// </summary>
    private static string ConstraintTemplate(ITypeParameterSymbol typeParameter, IReadOnlyList<ITypeParameterSymbol> own, ref string? foreign)
    {
        var parts = new List<string>();

        // Special constraints must come first (class, struct, unmanaged, notnull)
        // Note: struct implies notnull, and unmanaged implies struct
        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            parts.Add("unmanaged");
        }
        else if (typeParameter.HasValueTypeConstraint)
        {
            parts.Add("struct");
        }
        else if (typeParameter.HasReferenceTypeConstraint)
        {
            parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }
        else if (typeParameter.HasNotNullConstraint)
        {
            parts.Add("notnull");
        }

        // Type constraints (base class, interfaces)
        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            parts.Add(Template(constraintType, own, ref foreign));
        }

        // new() constraint must come last
        if (typeParameter.HasConstructorConstraint)
        {
            parts.Add("new()");
        }

        if (parts.Count == 0)
            return string.Empty;

        return $"where {{{IndexOf(own, typeParameter)}}} : {string.Join(", ", parts)}";
    }
}
