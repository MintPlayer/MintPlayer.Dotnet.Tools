#nullable enable

namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Marks a field or property to receive IOptions&lt;T&gt; for a configuration section.
/// The field type must be IOptions&lt;T&gt;, IOptionsSnapshot&lt;T&gt;, or IOptionsMonitor&lt;T&gt;.
/// </summary>
/// <remarks>
/// <para>
/// This attribute integrates with Microsoft.Extensions.Options for strongly-typed configuration.
/// The options interface is injected directly from the DI container.
/// </para>
/// <para>
/// Supported field types:
/// <list type="bullet">
/// <item><c>IOptions&lt;T&gt;</c> - Singleton, read once at startup</item>
/// <item><c>IOptionsSnapshot&lt;T&gt;</c> - Scoped, re-read per request</item>
/// <item><c>IOptionsMonitor&lt;T&gt;</c> - Singleton with change notifications</item>
/// </list>
/// </para>
/// <para>
/// The Section parameter is informational. Actual binding is done during service registration:
/// <c>services.Configure&lt;T&gt;(configuration.GetSection("SectionName"));</c>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public partial class EmailService
/// {
///     [Options("Email")]
///     private readonly IOptions&lt;EmailOptions&gt; emailOptions;
///
///     [Options("Email:RateLimits")]
///     private readonly IOptionsSnapshot&lt;RateLimitOptions&gt; rateLimitOptions;
///
///     [Options("Features")]
///     private readonly IOptionsMonitor&lt;FeatureFlags&gt; featureFlags;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class OptionsAttribute : Attribute
{
    /// <summary>
    /// The configuration section name to bind to the options type.
    /// If null or empty, binds to the root configuration.
    /// This is informational - actual binding is done via services.Configure&lt;T&gt;().
    /// </summary>
    public string? Section { get; }

    /// <summary>
    /// Creates an OptionsAttribute that binds to the specified configuration section.
    /// </summary>
    /// <param name="section">The configuration section name (e.g., "Email", "Database:Settings")</param>
    public OptionsAttribute(string? section = null)
    {
        Section = section;
    }
}
