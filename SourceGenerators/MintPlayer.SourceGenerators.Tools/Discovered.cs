using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// What discovery found for one target: the model to generate from (if any), and the diagnostics to report.
/// </summary>
/// <typeparam name="TModel">
/// The generator's model. It must be value-equatable (e.g. <c>[GenerateEquality]</c>), or every step after
/// discovery reports <c>Modified</c> on every run.
/// </typeparam>
/// <remarks>
/// The two are kept in one value so a transform can return both, and split again with
/// <see cref="DiscoveryExtensions.Models{TModel}"/> and <see cref="DiscoveryExtensions.Diagnostics{TModel}"/>.
/// After the split, the producer step sees only the model, so it stays cached when an edit above the type moves
/// a diagnostic's span but changes nothing that is generated.
/// </remarks>
public sealed class Discovered<TModel> : IEquatable<Discovered<TModel>>
    where TModel : class
{
    public Discovered(TModel? model, EquatableArray<DiagnosticInfo> diagnostics = default)
    {
        Model = model;
        Diagnostics = diagnostics;
    }

    /// <summary>Nothing to generate and nothing to report.</summary>
    public static readonly Discovered<TModel> None = new(null);

    /// <summary>Nothing to generate; report <paramref name="diagnostics"/>.</summary>
    public static Discovered<TModel> Fail(params DiagnosticInfo[] diagnostics) => new(null, diagnostics);

    /// <summary>Null when nothing is generated for the target, for instance because it is not partial.</summary>
    public TModel? Model { get; }

    /// <summary>Never default: a default array reads as empty.</summary>
    public EquatableArray<DiagnosticInfo> Diagnostics { get; }

    public bool Equals(Discovered<TModel>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<TModel?>.Default.Equals(Model, other.Model) && Diagnostics.Equals(other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as Discovered<TModel>);

    public override int GetHashCode() => ValueEquality.Combine(Model?.GetHashCode() ?? 0, Diagnostics.GetHashCode());

    public override string ToString() => $"{Model?.ToString() ?? "(no model)"} [{Diagnostics.Count} diagnostic(s)]";
}

/// <summary>
/// Discovery scaffolding over <see cref="Discovered{TModel}"/>: <see cref="ForAttribute{TModel}"/> to find the
/// targets, <see cref="Models{TModel}"/> and <see cref="Diagnostics{TModel}"/> to split the result, and
/// <see cref="GeneratorExtensions.ReportDiagnostics(IncrementalGeneratorInitializationContext, IncrementalValuesProvider{DiagnosticInfo}[])"/>
/// to report.
/// </summary>
/// <example>
/// <code>
/// var discovered = context.SyntaxProvider.ForAttribute(
///     "MyNamespace.MyAttribute",
///     static (node, ct) => node is TypeDeclarationSyntax,
///     static (ctx, ct) => Discover(ctx, ct));
///
/// context.ProduceCode(discovered.Models("My.Models").Select(static Producer (m, ct) => new MyProducer(m)));
/// context.ReportDiagnostics(discovered.Diagnostics());
/// </code>
/// </example>
public static class DiscoveryExtensions
{
    /// <summary>
    /// <see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}"/>, with the transform returning a
    /// <see cref="Discovered{TModel}"/>. A null result is dropped.
    /// </summary>
    /// <param name="provider"><c>context.SyntaxProvider</c>.</param>
    /// <param name="metadataName">The attribute's full metadata name, e.g. <c>"N.MyAttribute"</c>.</param>
    /// <param name="predicate">A cheap syntax filter, e.g. <c>node is TypeDeclarationSyntax</c>.</param>
    /// <param name="transform">Turns a target into a model and its diagnostics. It must not let a symbol, a
    /// <see cref="Location"/> or a syntax node escape into the result.</param>
    /// <remarks>
    /// <see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}"/> only visits declarations that carry the
    /// attribute, so unrelated code (a record, a struct, an interface) never reaches the transform. Prefer it over
    /// <c>CreateSyntaxProvider</c> whenever a marker attribute drives discovery.
    /// </remarks>
    public static IncrementalValuesProvider<Discovered<TModel>> ForAttribute<TModel>(
        this SyntaxValueProvider provider,
        string metadataName,
        Func<SyntaxNode, CancellationToken, bool> predicate,
        Func<GeneratorAttributeSyntaxContext, CancellationToken, Discovered<TModel>?> transform)
        where TModel : class
        => provider
            .ForAttributeWithMetadataName(metadataName, predicate, transform)
            .Where(static d => d is not null)
            .Select(static (d, ct) => d!);

    /// <summary>The non-null models, one per target, so each one caches on its own.</summary>
    /// <param name="provider">The discovery result.</param>
    /// <param name="trackingName">Optional tracking name for the step that selects the model; tests use it to
    /// assert that the step stays cached.</param>
    public static IncrementalValuesProvider<TModel> Models<TModel>(this IncrementalValuesProvider<Discovered<TModel>> provider, string? trackingName = null)
        where TModel : class
    {
        var models = provider.Select(static (d, ct) => d.Model);
        if (trackingName is not null) models = models.WithTrackingName(trackingName);
        return models
            .Where(static m => m is not null)
            .Select(static (m, ct) => m!);
    }

    /// <summary>Every diagnostic of every target, flattened. Empty (so no reporting step runs) when there are none.</summary>
    public static IncrementalValuesProvider<DiagnosticInfo> Diagnostics<TModel>(this IncrementalValuesProvider<Discovered<TModel>> provider)
        where TModel : class
        => provider.SelectMany(static (d, ct) => d.Diagnostics.AsImmutableArray());
}
