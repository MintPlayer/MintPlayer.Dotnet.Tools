namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Marks a string field or property to be populated from a connection string.
/// Uses IConfiguration.GetConnectionString() which reads from the "ConnectionStrings" section.
/// </summary>
/// <remarks>
/// <para>
/// This attribute can only be applied to string fields or properties.
/// The generator will automatically inject IConfiguration if not already present.
/// </para>
/// <para>
/// Required/optional behavior is inferred from the field's nullability:
/// <list type="bullet">
/// <item>Non-nullable <c>string</c> is required and throws if not found</item>
/// <item>Nullable <c>string?</c> is optional and returns null if not found</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public partial class DatabaseService
/// {
///     // Required - throws if not found
///     [ConnectionString("DefaultConnection")]
///     private readonly string connectionString;
///
///     // Optional - returns null if not found
///     [ConnectionString("ReadOnlyConnection")]
///     private readonly string? readOnlyConnectionString;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class ConnectionStringAttribute : Attribute
{
    /// <summary>
    /// The name of the connection string in the ConnectionStrings configuration section.
    /// This is passed to IConfiguration.GetConnectionString(name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a ConnectionStringAttribute that reads the specified connection string.
    /// </summary>
    /// <param name="name">The name of the connection string (e.g., "DefaultConnection")</param>
    public ConnectionStringAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}
