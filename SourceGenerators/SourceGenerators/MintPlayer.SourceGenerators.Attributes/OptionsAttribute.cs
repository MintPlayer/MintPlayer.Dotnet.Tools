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
/// The Section parameter is documentation only: the generator does not read it and binds nothing. Bind each options
/// type once, where it is registered:
/// <c>services.AddOptions&lt;T&gt;().BindConfiguration("SectionName");</c>
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
    /// The configuration section the options type is bound to, for documentation only. The generator does not read
    /// it; the binding is done where the options are registered, e.g.
    /// <c>services.AddOptions&lt;T&gt;().BindConfiguration("Section")</c>.
    /// </summary>
    public string? Section { get; }

    /// <summary>
    /// Creates an OptionsAttribute that documents the configuration section the options are bound to.
    /// </summary>
    /// <param name="section">The configuration section name (e.g., "Email", "Database:Settings"), for documentation only.</param>
    public OptionsAttribute(string? section = null)
    {
        Section = section;
    }
}
