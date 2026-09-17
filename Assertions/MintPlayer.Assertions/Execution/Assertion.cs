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
    public Assertion FailWith(string template, params object?[]? args)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, args));
        return this;
    }

    // ── Arity-specific overloads ────────────────────────────────────────────────────────────────
    //
    // These exist for one reason, and it is not convenience: `params object?[]` builds its array AT
    // THE CALL SITE, before the call. So a passing assertion — which returns on the very first line
    // of FailWith — still paid for an array, plus a box for every value-type argument in it.
    //
    // Measured over a passing `42.Should().Be(42)`: 112 B/op above a bare Should(). That is the
    // whole cost of an assertion that does nothing but compare two ints and return.
    //
    // The overloads are generic, so nothing boxes on the way in, and the array is built only after
    // the condition has already failed — on a path that is about to throw anyway.
    //
    // ⚠️ No call site had to change. C# prefers a generic overload with an exact arity over the
    // params form, so every existing `FailWith("...{0}...", x)` binds here automatically. Keep it
    // that way: adding an argument to a call site silently moves it back onto the params overload
    // once it exceeds the highest arity below, which is why the count goes to three rather than two.
    // PassingPathAllocationTests is what notices if that happens.

    /// <summary>Reports a failure with no arguments; nothing to allocate at all.</summary>
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

    /// <inheritdoc cref="FailWith(string, object?[])"/>
    public Assertion FailWith<T0, T1, T2, T3>(string template, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        if (condition) return this;
        condition = true;
        AssertionScope.ReportFailure(RenderMessage(template, [arg0, arg1, arg2, arg3]));
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
