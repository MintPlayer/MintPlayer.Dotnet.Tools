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
