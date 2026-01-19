using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// Contains information about generic type parameters and constraints for a service registration.
/// </summary>
[AutoValueComparer]
public partial class GenericTypeInfo
{
    /// <summary>
    /// Type parameter names (e.g., ["TEntity", "TKey"]).
    /// </summary>
    public string[] TypeParameterNames { get; set; } = [];

    /// <summary>
    /// Full constraint clauses (e.g., ["where TEntity : EntityBase&lt;TKey&gt;", "where TKey : IEquatable&lt;TKey&gt;"]).
    /// </summary>
    public string[] ConstraintClauses { get; set; } = [];

    /// <summary>
    /// Service type with type parameters (e.g., "global::Namespace.IGenericRepository&lt;TEntity, TKey&gt;").
    /// </summary>
    public string GenericServiceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Implementation type with type parameters (e.g., "global::Namespace.GenericRepository&lt;TEntity, TKey&gt;").
    /// </summary>
    public string GenericImplementationTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The simple name of the implementation type without namespace (e.g., "GenericRepository").
    /// Used for generating method name suffixes when constraints conflict.
    /// </summary>
    public string ImplementationSimpleName { get; set; } = string.Empty;
}
