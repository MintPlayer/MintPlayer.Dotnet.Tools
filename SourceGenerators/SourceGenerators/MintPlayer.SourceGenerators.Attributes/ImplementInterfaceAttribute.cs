namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Adds the interfaces to the partial type's base list and implements every member that is still unimplemented
/// and has an obvious body: read/write properties become auto-properties.
/// </summary>
/// <remarks>
/// <para>Members written by hand, or inherited from a base class, always win; the generator fills only the gaps.</para>
/// <para>On an abstract type, every other member (methods, get-only and set-only properties, events, indexers) is
/// declared <c>public abstract</c>, so the concrete subclass supplies it. On a concrete type those members are a
/// compile error (IMPLINTF003, IMPLINTF004) unless they are implemented by hand.</para>
/// </remarks>
/// <example>
/// <code>
/// [ImplementInterface(typeof(ICustomerManagement), typeof(IWarehouseManagement))]
/// public partial class CarDealerContext { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class ImplementInterfaceAttribute : Attribute
{
    /// <param name="interfaces">The interfaces to implement. Closed generic interfaces are allowed; open ones
    /// (<c>typeof(IRepository&lt;&gt;)</c>) are not.</param>
    public ImplementInterfaceAttribute(params Type[] interfaces) { }

    /// <summary>
    /// Implements this attribute's interfaces explicitly (<c>T IFoo.Name { get; set; }</c>) instead of with public
    /// members. Applies to the interfaces of this attribute only; use a second attribute for a mix.
    /// </summary>
    public bool Explicit { get; set; }

    /// <summary>
    /// Initialises non-nullable reference-type properties with <c>= default!</c> (the default). Set to false to let
    /// CS8618 report the properties a constructor does not assign.
    /// </summary>
    public bool InitializeNonNullable { get; set; } = true;
}

/// <summary>
/// The generic form of <see cref="ImplementInterfaceAttribute"/>: <c>[ImplementInterface&lt;IFoo&gt;]</c>.
/// Requires C# 11 or later.
/// </summary>
/// <typeparam name="TInterface">The interface to implement.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class ImplementInterfaceAttribute<TInterface> : Attribute
{
    /// <inheritdoc cref="ImplementInterfaceAttribute.Explicit"/>
    public bool Explicit { get; set; }

    /// <inheritdoc cref="ImplementInterfaceAttribute.InitializeNonNullable"/>
    public bool InitializeNonNullable { get; set; } = true;
}
