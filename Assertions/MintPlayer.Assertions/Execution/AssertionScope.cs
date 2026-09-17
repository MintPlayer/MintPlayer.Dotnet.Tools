// Root namespace, not MintPlayer.Assertions.Execution: soft assertions are an everyday feature,
// and requiring a second using for `new AssertionScope(...)` was the only boilerplate left in the
// consumer-facing API. The rest of Execution (the Assertion builder) is for extension authors and
// stays there. Types under MintPlayer.Assertions.* still see this via enclosing-namespace lookup.
namespace MintPlayer.Assertions;

/// <summary>
/// Collects assertion failures instead of throwing on the first one ("soft assertions").
/// All failures inside the scope are combined into a single <see cref="AssertionFailedException"/>
/// when the outermost scope is disposed. Scopes nest; a nested scope bubbles its failures
/// (prefixed with its context, if any) into its parent.
/// </summary>
/// <example>
/// using (new AssertionScope("the response"))
/// {
///     response.Status.Should().Be(200);
///     response.Body.Should().NotBeEmpty();
/// } // throws once, listing every failure
/// </example>
public sealed class AssertionScope : IDisposable
{
    private static readonly AsyncLocal<AssertionScope?> current = new();

    private readonly AssertionScope? parent;
    private readonly string? context;
    private readonly List<string> failures = [];

    /// <summary>Lazily created: most scopes attach nothing, and an empty list per scope is waste.</summary>
    private List<(string Name, Func<string> ValueFactory)>? reportables;

    private bool disposed;

    public AssertionScope() : this(null) { }

    public AssertionScope(string? context)
    {
        parent = current.Value;
        this.context = context;
        current.Value = this;
    }

    /// <summary>The innermost active scope on the current async flow, or null when none is active.</summary>
    public static AssertionScope? Current => current.Value;

    /// <summary>True when at least one failure has been collected in this scope.</summary>
    public bool HasFailures => failures.Count > 0;

    /// <summary>The failures collected so far, in the order they were reported.</summary>
    /// <remarks>
    /// A snapshot, not a view: the scope keeps collecting after this is read, and handing out the
    /// live list would let a caller mutate what gets thrown.
    /// </remarks>
    public IReadOnlyList<string> Failures => [.. failures];

    /// <summary>
    /// Takes the collected failures and clears them, so disposing the scope throws nothing.
    /// </summary>
    /// <remarks>
    /// This is how you assert that something <i>did</i> fail without the failure escaping — testing
    /// an assertion library, or a helper that deliberately probes for a mismatch. It returns the
    /// failures rather than discarding them silently, because a <c>Discard()</c> whose result is
    /// ignored is indistinguishable from a bug that swallowed a real failure, and returning them
    /// makes the intent visible at the call site.
    /// </remarks>
    public IReadOnlyList<string> Discard()
    {
        var discarded = (IReadOnlyList<string>)[.. failures];
        failures.Clear();
        return discarded;
    }

    /// <summary>
    /// Adds <paramref name="message"/> to this scope exactly as written, bypassing the formatting
    /// every assertion normally applies.
    /// </summary>
    /// <remarks>
    /// For a custom assertion that has already built its own message — a diff, a table, a rendered
    /// tree — and would only be damaged by being fed through the template machinery again.
    /// <para>
    /// ⚠️ Named <c>AddPreFormattedFailure</c>, not <c>AddFailure</c>, and deliberately so: it takes a
    /// bare string and does no substitution, so a caller who passes a template with <c>{0}</c> in it
    /// gets the braces verbatim. A name that did not say "pre-formatted" would make that a surprise.
    /// </para>
    /// </remarks>
    public void AddPreFormattedFailure(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        failures.Add(message);
    }

    /// <summary>
    /// Attaches context that is appended to this scope's failure report — a correlation id, the
    /// request that was being replayed, the seed a generator used.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The value is a <see cref="Func{TResult}"/>, not a string, and that is the whole point.</b>
    /// Reportables exist to be read when something fails, and a passing scope must not pay to build
    /// text nobody reads. The overload taking a string is there for values already in hand; prefer
    /// this one whenever producing the value costs anything at all.
    /// <para>
    /// A nested scope hands its reportables up to its parent along with its failures, so the
    /// outermost scope — the one that actually throws — reports everything attached anywhere inside
    /// it. Attaching to a scope that ends up reporting no failures costs nothing: the factory is
    /// never called.
    /// </para>
    /// </remarks>
    public void AddReportable(string name, Func<string> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueFactory);
        (reportables ??= []).Add((name, valueFactory));
    }

    /// <summary>Attaches an already-computed value to this scope's failure report.</summary>
    /// <remarks>See the remark on <see cref="AddReportable(string, Func{string})"/> before reaching for this.</remarks>
    public void AddReportable(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        AddReportable(name, () => value);
    }

    /// <summary>
    /// Routes an assertion failure: collected when a scope is active, thrown immediately otherwise.
    /// This is the single funnel every assertion in the library reports through.
    /// </summary>
    public static void ReportFailure(string message)
    {
        if (current.Value is { } scope)
            scope.failures.Add(message);
        else
            throw new AssertionFailedException(message);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        current.Value = parent;

        if (failures.Count == 0) return;

        var messages = context is null ? failures : failures.ConvertAll(f => $"[{context}] {f}");
        if (parent is not null)
        {
            parent.failures.AddRange(messages);
            // A nested scope hands its reportables up with its failures: the outermost scope is what
            // throws, so context attached inside would otherwise be lost at exactly the moment it
            // becomes useful.
            if (reportables is not null)
                (parent.reportables ??= []).AddRange(reportables);
            return;
        }

        throw new AssertionFailedException(string.Join(Environment.NewLine + Environment.NewLine, messages) + RenderReportables());
    }

    private string RenderReportables()
    {
        if (reportables is null || reportables.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine().AppendLine().Append("With:");
        foreach (var (name, valueFactory) in reportables)
        {
            sb.AppendLine().Append("  ").Append(name).Append(": ");
            // Reportables are diagnostics. One that throws must not replace the failure it was meant
            // to explain — the assertion message is the thing the reader actually needs.
            try { sb.Append(valueFactory()); }
            catch (Exception ex) { sb.Append("<threw ").Append(ex.GetType().Name).Append('>'); }
        }
        return sb.ToString();
    }
}
