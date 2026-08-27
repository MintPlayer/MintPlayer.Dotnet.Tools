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
            parent.failures.AddRange(messages);
        else
            throw new AssertionFailedException(string.Join(Environment.NewLine + Environment.NewLine, messages));
    }
}
