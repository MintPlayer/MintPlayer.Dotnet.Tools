#nullable enable

namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Marks a field or property to be populated from IConfiguration at construction time.
/// The generator will automatically inject IConfiguration and read the specified key.
/// </summary>
/// <remarks>
/// <para>
/// Required/optional behavior is inferred from the field's nullability:
/// <list type="bullet">
/// <item>Non-nullable types (e.g., <c>string</c>, <c>int</c>) are required and throw if not found, unless DefaultValue is specified</item>
/// <item>Nullable types (e.g., <c>string?</c>, <c>int?</c>) are optional and return null if not found</item>
/// </list>
/// </para>
/// <para>
/// Supported types:
/// <list type="bullet">
/// <item>Primitives: string, int, long, short, byte, uint, ulong, ushort, sbyte, float, double, decimal, bool, char</item>
/// <item>Nullable primitives: int?, bool?, etc.</item>
/// <item>Enums: Any enum type (parsed via Enum.Parse)</item>
/// <item>Date/Time: DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly</item>
/// <item>Other: Guid, Uri</item>
/// <item>Complex types: POCO classes (bound via GetSection().Get&lt;T&gt;())</item>
/// <item>Collections: T[], List&lt;T&gt;, IEnumerable&lt;T&gt;</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public partial class MyService
/// {
///     // Required - throws if not found
///     [Config("Database:ConnectionString")]
///     private readonly string connectionString;
///
///     // Optional - returns null if not found
///     [Config("Database:BackupConnection")]
///     private readonly string? backupConnection;
///
///     // Non-nullable with default - uses default if not found
///     [Config("Database:MaxRetries", DefaultValue = 3)]
///     private readonly int maxRetries;
///
///     // Required enum
///     [Config("Database:Type")]
///     private readonly DatabaseType databaseType;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class ConfigAttribute : Attribute
{
    /// <summary>
    /// The configuration key path (e.g., "Database:Type" or "Logging:LogLevel:Default").
    /// Uses the same colon-separated format as IConfiguration indexer.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Default value when the configuration key is not found.
    /// When specified on a non-nullable field, makes the field optional (uses default instead of throwing).
    /// Must be a compile-time constant. Type must match or be convertible to field type.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Creates a ConfigAttribute that reads from the specified configuration key.
    /// </summary>
    /// <param name="key">The configuration key path (e.g., "Database:Type")</param>
    public ConfigAttribute(string key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }
}
