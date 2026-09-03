using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// Represents a service registration extracted from a [Register] attribute.
/// </summary>
[AutoValueComparer]
public partial class ServiceRegistration
{
    /// <summary>
    /// The fully-qualified name of the service type (interface or class).
    /// Null when registering a class as itself (self-registration).
    /// </summary>
    public string? ServiceTypeName { get; set; }

    /// <summary>
    /// The fully-qualified name of the implementation type.
    /// </summary>
    public string? ImplementationTypeName { get; set; }

    /// <summary>
    /// The service lifetime (Singleton, Scoped, or Transient).
    /// </summary>
    public ServiceLifetime Lifetime { get; set; }

    /// <summary>
    /// Optional hint for the generated extension method name.
    /// When null, the default method name based on assembly name is used.
    /// </summary>
    public string? MethodNameHint { get; set; } = string.Empty;

    /// <summary>
    /// Ready-to-emit argument expressions for the static factories marked with
    /// <c>[RegisterFactory]</c> that create this service — for example
    /// <c>global::Demo.Greeter.Create</c> or <c>sp =&gt; global::Demo.Greeter.Create()</c>.
    /// </summary>
    /// <remarks>
    /// Full expressions rather than bare method names, because whether a factory can be passed as
    /// a method group depends on its signature, and only the generator has the symbol information
    /// to decide. A parameterless factory has to be wrapped in a lambda: the
    /// <c>AddScoped&lt;T&gt;</c> overload takes <c>Func&lt;IServiceProvider, T&gt;</c>, so passing
    /// the method group directly produces CS1503 in the consumer's build.
    /// </remarks>
    public string[] FactoryExpressions { get; set; } = [];

    /// <summary>
    /// The accessibility level of the generated extension method (Public or Internal).
    /// </summary>
    public Attributes.EGeneratedAccessibility Accessibility { get; set; }

    /// <summary>
    /// Indicates whether this registration is for an open generic type.
    /// </summary>
    public bool IsGeneric { get; set; }

    /// <summary>
    /// Contains type parameter and constraint information for generic registrations.
    /// Null when IsGeneric is false.
    /// </summary>
    public GenericTypeInfo? GenericInfo { get; set; }

    /// <summary>
    /// Indicates where the [Register] attribute was applied.
    /// </summary>
    public ERegistrationAppliedOn AppliedOn { get; set; }

    /// <summary>
    /// Indicates whether there was an error with the attribute usage.
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>
    /// Location of the attribute for diagnostic reporting.
    /// </summary>
    public LocationKey? Location { get; set; }
}

/// <summary>
/// Indicates where a [Register] attribute was applied.
/// </summary>
public enum ERegistrationAppliedOn
{
    /// <summary>
    /// The [Register] attribute was applied to a class declaration.
    /// </summary>
    Class,

    /// <summary>
    /// The [Register] attribute was applied at the assembly level.
    /// </summary>
    Assembly,
}
