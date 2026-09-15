using System.Text;
using MintPlayer.Assertions.Formatting;

namespace MintPlayer.Assertions.Execution;

/// <summary>
/// The chainable failure builder every assertion is written with — the library's stable
/// extensibility surface (this API will not break across versions).
/// </summary>
/// <remarks>
/// Template placeholders in <see cref="FailWith"/>:
/// <c>{subject}</c> — the caller's expression text (from CallerArgumentExpression);
/// <c>{reason}</c> — the woven "because" clause (empty when none was given, otherwise " because ...");
/// <c>{0}</c>, <c>{1}</c>, … — arguments rendered through <see cref="Formatter"/>.
/// </remarks>
/// <example>
/// Assertion.For(subjectExpression)
///     .ForCondition(actual == expected)
///     .BecauseOf(because, becauseArgs)
///     .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, actual);
/// </example>
public struct Assertion
{
    private readonly string subject;
    private bool condition;
    private string? because;
    private object?[]? becauseArgs;

    private Assertion(string subject)
    {
        this.subject = subject;
        condition = true;
    }

    /// <summary>Starts an assertion for the given caller expression (falls back to "value").</summary>
    public static Assertion For(string? subjectExpression) => new(string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!);

    /// <summary>The condition that must hold; when false, the next <see cref="FailWith"/> reports a failure.</summary>
    public Assertion ForCondition(bool condition)
    {
        this.condition = condition;
        return this;
    }

    /// <summary>Supplies the user's reason, woven into the message as the {reason} placeholder.</summary>
    public Assertion BecauseOf(string? because, params object?[]? becauseArgs)
    {
        this.because = because;
        this.becauseArgs = becauseArgs;
        return this;
    }

    /// <summary>
    /// Reports a failure when the current condition is false, then resets the condition so the
    /// chain can continue with independent checks. Failures go through
    /// <see cref="AssertionScope.ReportFailure"/> (collected in a scope, thrown otherwise).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arity-specific generic overloads below exist for performance and are what nearly every
    /// call site binds to. This <c>params</c> form allocates its <c>object?[]</c> — and boxes every
    /// value-type argument into it — at the CALL SITE, which means a passing assertion pays for a
    /// failure message it will never render. The generic overloads take their arguments directly and
    /// build the array only after the condition has already failed, so the passing path allocates
    /// nothing and boxes nothing.
    /// </para>
    /// <para>
    /// Arity 0-3 covers 339 of the 366 call sites in this library. Anything wider falls back here,
    /// which is fine: those are rare, and the cost is one array on a path that is already about to
    /// throw.
    /// </para>
    /// <para>
    /// ⚠️ Passing a single <c>object?[]</c> variable to this method binds it to
    /// <see cref="FailWith{T0}(string, T0)"/> instead, rendering the array as ONE argument. Spread
    /// the values, or cast to <c>object?[]</c> explicitly. No call site in this library does that
    /// today.
    /// </para>
    /// </remarks>
    public Assertion FailWith(string template, params object?[]? args)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, args));
        return this;
    }

    /// <inheritdoc cref="FailWith(string, object?[])"/>
    public Assertion FailWith(string template)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, null));
        return this;
    }

    /// <inheritdoc cref="FailWith(string, object?[])"/>
    public Assertion FailWith<T0>(string template, T0 arg0)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, [arg0]));
        return this;
    }

    /// <inheritdoc cref="FailWith(string, object?[])"/>
    public Assertion FailWith<T0, T1>(string template, T0 arg0, T1 arg1)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, [arg0, arg1]));
        return this;
    }

    /// <inheritdoc cref="FailWith(string, object?[])"/>
    public Assertion FailWith<T0, T1, T2>(string template, T0 arg0, T1 arg1, T2 arg2)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, [arg0, arg1, arg2]));
        return this;
    }

    private readonly string RenderMessage(string template, object?[]? args)
    {
        var sb = new StringBuilder(template.Length + 64);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c != '{') { sb.Append(c); continue; }

            // Escaped "{{"
            if (i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i++; continue; }

            var close = template.IndexOf('}', i + 1);
            if (close < 0) { sb.Append(c); continue; }

            var token = template.Substring(i + 1, close - i - 1);
            if (token == "subject")
            {
                sb.Append(subject);
            }
            else if (token == "reason")
            {
                sb.Append(RenderReason());
            }
            else if (int.TryParse(token, out var index) && args is not null && index >= 0 && index < args.Length)
            {
                sb.Append(Formatter.Format(args[index]));
            }
            else
            {
                sb.Append('{').Append(token).Append('}');
            }
            i = close;
        }
        return sb.ToString();
    }

    private readonly string RenderReason()
    {
        if (string.IsNullOrWhiteSpace(because)) return string.Empty;

        var reason = because!.Trim();
        if (becauseArgs is { Length: > 0 })
        {
            try { reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, reason, becauseArgs); }
            catch (FormatException) { /* keep the raw reason; a malformed format string must not mask the real failure */ }
        }

        return reason.StartsWith("because", StringComparison.OrdinalIgnoreCase)
            ? " " + reason
            : " because " + reason;
    }
}
