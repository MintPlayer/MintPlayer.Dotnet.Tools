using MintPlayer.Assertions.Formatting;

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
    private List<IValueFormatter>? valueFormatters;
    private List<KeyValuePair<string, Func<string>>>? reportables;
    private FormattingOptions? formattingOptions;
    private bool disposed;

    /// <summary>Starts a scope that collects failures until it is disposed.</summary>
    public AssertionScope() : this(null) { }

    /// <summary>Starts a scope whose failures are prefixed with <paramref name="context"/>.</summary>
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

    /// <summary>
    /// How values are rendered inside this scope. Falls back to the enclosing scope, then to
    /// <see cref="Formatter.Options"/>.
    /// </summary>
    public FormattingOptions FormattingOptions
    {
        get => formattingOptions ?? parent?.FormattingOptions ?? Formatter.Options;
        set => formattingOptions = value;
    }

    /// <summary>Formatters registered for this scope, innermost first, or null when none are.</summary>
    internal IReadOnlyList<IValueFormatter>? ValueFormatters
    {
        get
        {
            var inherited = parent?.ValueFormatters;
            if (valueFormatters is null) return inherited;
            if (inherited is null) return valueFormatters;
            return [.. valueFormatters, .. inherited];
        }
    }

    /// <summary>
    /// Renders values with <paramref name="formatter"/> for as long as this scope is open.
    /// </summary>
    /// <remarks>
    /// The scoped alternative to <see cref="Formatter.Register"/>: nothing has to be unregistered,
    /// and a formatter meant for one comparison cannot leak into the rest of the suite.
    /// </remarks>
    public AssertionScope Using(IValueFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        (valueFormatters ??= []).Add(formatter);
        return this;
    }

    /// <summary>
    /// Sets the formatting options for this scope: <c>new AssertionScope().WithFormatting(o =&gt; o with { UseLineBreaks = true })</c>.
    /// </summary>
    public AssertionScope WithFormatting(Func<FormattingOptions, FormattingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        FormattingOptions = configure(FormattingOptions);
        return this;
    }

    /// <summary>
    /// Attaches a named piece of context that is appended to the failure message — but only if this
    /// scope actually fails.
    /// </summary>
    /// <remarks>
    /// The lazy overload is the point: a reportable is usually something expensive to produce (the
    /// whole request body, a rendered diff, a database snapshot) and pointless to produce when
    /// everything passes. Nothing here runs in a green suite.
    /// </remarks>
    public AssertionScope AddReportable(string key, Func<string> valueFactory)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(valueFactory);
        (reportables ??= []).Add(new(key, valueFactory));
        return this;
    }

    /// <summary>The eager overload of <see cref="AddReportable(string, Func{string})"/>.</summary>
    public AssertionScope AddReportable(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddReportable(key, () => value);
    }

    /// <summary>
    /// Adds a failure message verbatim, without any of the template rendering the assertions do.
    /// </summary>
    /// <remarks>
    /// For an extension that has already built its message — a multi-line diff, most of all — and
    /// would only have to escape it back out of the template syntax.
    /// </remarks>
    public void AddPreFormattedFailure(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        failures.Add(message);
    }

    /// <summary>
    /// Takes the failures collected so far and clears them, so the scope will not throw for them.
    /// </summary>
    /// <remarks>
    /// The building block for an assertion that tries something and reports its own message when it
    /// does not hold — <c>BeEquivalentTo</c>'s full-diff block is exactly this shape. Without it the
    /// only way to probe was to catch the exception, which a scope makes impossible because it
    /// collects instead of throwing.
    /// </remarks>
    public IReadOnlyList<string> Discard()
    {
        if (failures.Count == 0) return [];
        var discarded = failures.ToArray();
        failures.Clear();
        return discarded;
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
            throw AssertionConfiguration.BuildException(message);
    }

    /// <summary>Ends the scope, throwing a single exception for everything it collected.</summary>
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
            // The reportables travel with the failures: the outermost scope is where the message is
            // built, and context gathered here is exactly what explains a failure reported there.
            if (reportables is not null) (parent.reportables ??= []).AddRange(reportables);
            return;
        }

        throw AssertionConfiguration.BuildException(BuildMessage(messages));
    }

    private string BuildMessage(List<string> messages)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, messages);
        if (reportables is null || reportables.Count == 0) return text;

        var sb = new System.Text.StringBuilder(text);
        foreach (var (key, valueFactory) in reportables)
        {
            string value;
            try
            {
                value = valueFactory();
            }
            catch (Exception ex)
            {
                // A reportable that throws must not replace the failure it was meant to explain.
                value = $"<threw {ex.GetType().Name}: {ex.Message}>";
            }
            sb.Append(Environment.NewLine).Append(Environment.NewLine)
              .Append("With ").Append(key).Append(':').Append(Environment.NewLine)
              .Append(value);
        }
        return sb.ToString();
    }
}
