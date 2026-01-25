using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// Represents a field marked with [Config] attribute.
/// </summary>
[AutoValueComparer]
public partial class ConfigField
{
    /// <summary>
    /// The configuration key path (e.g., "Database:Type").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The fully qualified type of the field.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The field or property name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The default value when not found. Null if not specified.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Whether a default value was explicitly specified in the attribute.
    /// </summary>
    public bool HasDefaultValue { get; set; }

    /// <summary>
    /// Whether the field type is nullable (e.g., int?, string?).
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Whether the field type is an enum.
    /// </summary>
    public bool IsEnum { get; set; }

    /// <summary>
    /// Whether the field type is a complex type (POCO) that needs GetSection().Get&lt;T&gt;().
    /// </summary>
    public bool IsComplexType { get; set; }

    /// <summary>
    /// Whether the field type is a collection (array, List&lt;T&gt;, etc.).
    /// </summary>
    public bool IsCollection { get; set; }

    /// <summary>
    /// The element type for collections, or the underlying type for nullable enums.
    /// </summary>
    public string? ElementOrUnderlyingType { get; set; }

    /// <summary>
    /// The category of type for code generation purposes.
    /// </summary>
    public ConfigFieldTypeCategory TypeCategory { get; set; }
}

/// <summary>
/// Represents a field marked with [ConnectionString] attribute.
/// </summary>
[AutoValueComparer]
public partial class ConnectionStringField
{
    /// <summary>
    /// The connection string name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The field or property name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the field type is nullable string.
    /// If true, the field is optional. If false, it's required.
    /// </summary>
    public bool IsNullable { get; set; }
}

/// <summary>
/// Represents a field marked with [Options] attribute.
/// </summary>
[AutoValueComparer]
public partial class OptionsField
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public string? Section { get; set; }

    /// <summary>
    /// The fully qualified type of the field (e.g., "Microsoft.Extensions.Options.IOptions&lt;MyOptions&gt;").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The field or property name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The options type T from IOptions&lt;T&gt;.
    /// </summary>
    public string OptionsType { get; set; } = string.Empty;

    /// <summary>
    /// The kind of options interface (IOptions, IOptionsSnapshot, IOptionsMonitor).
    /// </summary>
    public OptionsFieldKind Kind { get; set; }
}

/// <summary>
/// Categories of types supported by [Config] attribute.
/// </summary>
public enum ConfigFieldTypeCategory
{
    /// <summary>String type - direct assignment.</summary>
    String,
    /// <summary>Numeric types (int, long, double, etc.) - use Parse.</summary>
    Numeric,
    /// <summary>Boolean type - use bool.Parse.</summary>
    Boolean,
    /// <summary>Character type - use value[0].</summary>
    Char,
    /// <summary>Enum type - use Enum.Parse&lt;T&gt;.</summary>
    Enum,
    /// <summary>DateTime, DateTimeOffset - use DateTime.Parse with InvariantCulture.</summary>
    DateTime,
    /// <summary>TimeSpan - use TimeSpan.Parse with InvariantCulture.</summary>
    TimeSpan,
    /// <summary>DateOnly (.NET 6+) - use DateOnly.Parse with InvariantCulture.</summary>
    DateOnly,
    /// <summary>TimeOnly (.NET 6+) - use TimeOnly.Parse with InvariantCulture.</summary>
    TimeOnly,
    /// <summary>Guid - use Guid.Parse.</summary>
    Guid,
    /// <summary>Uri - use new Uri().</summary>
    Uri,
    /// <summary>Complex types - use GetSection().Get&lt;T&gt;().</summary>
    Complex,
    /// <summary>Collection types - use GetSection().Get&lt;T[]&gt;().</summary>
    Collection,
    /// <summary>Unsupported type.</summary>
    Unsupported
}

/// <summary>
/// The kind of options interface.
/// </summary>
public enum OptionsFieldKind
{
    /// <summary>IOptions&lt;T&gt; - singleton, read once.</summary>
    Options,
    /// <summary>IOptionsSnapshot&lt;T&gt; - scoped, re-read per request.</summary>
    OptionsSnapshot,
    /// <summary>IOptionsMonitor&lt;T&gt; - singleton with change notifications.</summary>
    OptionsMonitor
}
