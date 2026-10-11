using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// Contains information about generic type parameters and constraints for a service registration.
/// </summary>
[GenerateEquality]
public partial class GenericTypeInfo
{
    /// <summary>
    /// Type parameter names (e.g., ["TEntity", "TKey"]).
    /// </summary>
    public string[] TypeParameterNames { get; set; } = [];

    /// <summary>
    /// Constraint clauses as templates: type parameters are positional placeholders, so that registrations whose type
    /// parameters have different names compare and combine by position (e.g., ["where {0} : EntityBase&lt;{1}&gt;"]).
    /// </summary>
    public string[] ConstraintClauses { get; set; } = [];

    /// <summary>
    /// Service type as a template over the implementation's type parameters, built from the type arguments the
    /// implementation actually passes (e.g., "global::Namespace.IMap&lt;{1}, {0}&gt;" for <c>Map&lt;TValue, TKey&gt; : IMap&lt;TKey, TValue&gt;</c>).
    /// </summary>
    public string GenericServiceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Implementation type as a template (e.g., "global::Namespace.GenericRepository&lt;{0}, {1}&gt;").
    /// </summary>
    public string GenericImplementationTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The simple name of the implementation type without namespace (e.g., "GenericRepository").
    /// Used for generating method name suffixes when constraints conflict.
    /// </summary>
    public string ImplementationSimpleName { get; set; } = string.Empty;
}
