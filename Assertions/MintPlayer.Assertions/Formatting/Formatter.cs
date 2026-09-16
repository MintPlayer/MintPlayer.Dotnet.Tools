using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Formatting;

/// <summary>
/// Renders any value into failure-message text: cycle-safe, depth-limited, truncating.
/// Complex objects prefer source-generated member accessors from
/// <see cref="EquivalencyRegistry"/> (AOT-safe); reflection is a best-effort fallback whose
/// absence under trimming only reduces message detail, never correctness.
/// </summary>
/// <remarks>
/// Everything here runs on the failure path only — <c>FailWith</c> returns before calling it when
/// the condition holds — which is what makes custom formatters and richer output free under §0 of
/// the Phase 2 PRD.
/// </remarks>
public static class Formatter
{
    private static readonly List<IValueFormatter> globalFormatters = [];
    private static readonly Lock registrationLock = new();

    /// <summary>
    /// The options every assertion formats with, unless a scope overrides them.
    /// </summary>
    /// <remarks>
    /// Settable so a test project can widen the defaults once in a fixture rather than at every call
    /// site. A scope's own options take precedence; see <see cref="AssertionScope.FormattingOptions"/>.
    /// </remarks>
    public static FormattingOptions Options { get; set; } = FormattingOptions.Default;

    /// <summary>Adds a formatter consulted for every assertion, after any scope-registered ones.</summary>
    public static void Register(IValueFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (registrationLock) globalFormatters.Add(formatter);
    }

    /// <summary>Removes a previously registered global formatter. Returns false when it was not registered.</summary>
    public static bool Unregister(IValueFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (registrationLock) return globalFormatters.Remove(formatter);
    }

    /// <summary>Removes every global formatter. For a test fixture's teardown.</summary>
    public static void ClearFormatters()
    {
        lock (registrationLock) globalFormatters.Clear();
    }

    /// <summary>Renders <paramref name="value"/> using the ambient options.</summary>
    public static string Format(object? value) => Format(value, AssertionScope.Current?.FormattingOptions ?? Options);

    /// <summary>Renders <paramref name="value"/> using the given options.</summary>
    public static string Format(object? value, FormattingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sb = new StringBuilder();
        FormatInto(sb, value, 0, [], options);
        return Truncate(sb.ToString(), options);
    }

    /// <summary>Cuts the rendered text at <see cref="FormattingOptions.MaxLines"/>, saying how much was cut.</summary>
    private static string Truncate(string text, FormattingOptions options)
    {
        if (options.MaxLines <= 0) return text;

        var lines = 0;
        var index = 0;
        while (index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0) break;
            lines++;
            index = next + 1;
            if (lines == options.MaxLines)
            {
                var remaining = 1;
                for (var i = index; i < text.Length; i++)
                {
                    if (text[i] == '\n') remaining++;
                }
                return text[..next] + $"{Environment.NewLine}… ({remaining} more line(s); raise FormattingOptions.MaxLines)";
            }
        }
        return text;
    }

    private static void FormatInto(StringBuilder sb, object? value, int depth, HashSet<object> seen, FormattingOptions options)
    {
        if (value is not null && TryCustomFormat(sb, value, depth, seen, options)) return;

        switch (value)
        {
            case null:
                sb.Append("<null>");
                return;
            case string s:
                AppendQuoted(sb, s, options);
                return;
            case char c:
                sb.Append('\'').Append(c).Append('\'');
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case Enum e:
                sb.Append(e.GetType().Name).Append('.').Append(e);
                return;
            case DateTime dt:
                sb.Append(dt.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dto:
                sb.Append(dto.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateOnly d:
                sb.Append(d.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeOnly t:
                sb.Append(t.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeSpan ts:
                sb.Append(ts.ToString(null, CultureInfo.InvariantCulture));
                return;
            case Guid g:
                sb.Append(g.ToString("D"));
                return;
            case Type type:
                sb.Append(type.FullName ?? type.Name);
                return;
            case IFormattable f when IsNumeric(value):
                sb.Append(f.ToString(null, CultureInfo.InvariantCulture));
                return;
        }

        var runtimeType = value.GetType();

        // Cycle / depth guards (reference types only; value types cannot cycle)
        if (!runtimeType.IsValueType)
        {
            if (!seen.Add(value))
            {
                sb.Append("{Cyclic reference to ").Append(runtimeType.Name).Append('}');
                return;
            }
            if (depth > options.MaxDepth)
            {
                // Names the knob: "Name {…}" told the reader something was elided but not what to
                // do about it, and the depth limit is the single most common reason a failure
                // message does not show the member that actually differs.
                sb.Append(runtimeType.Name)
                  .Append(" {… depth ").Append(options.MaxDepth)
                  .Append(" reached; raise FormattingOptions.MaxDepth}");
                seen.Remove(value);
                return;
            }
        }

        try
        {
            if (value is IDictionary dictionary)
            {
                FormatDictionary(sb, dictionary, depth, seen, options);
            }
            else if (value is IEnumerable enumerable)
            {
                FormatEnumerable(sb, enumerable, depth, seen, options);
            }
            else if (OverridesToString(runtimeType))
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else
            {
                FormatMembers(sb, value, runtimeType, depth, seen, options);
            }
        }
        finally
        {
            if (!runtimeType.IsValueType)
                seen.Remove(value);
        }
    }

    /// <summary>
    /// Offers the value to the scope's formatters, then the global ones. First match wins.
    /// </summary>
    /// <remarks>
    /// A formatter that throws is skipped rather than allowed to replace the assertion's failure
    /// with its own: the caller is already looking at a failing assertion, and losing the message
    /// that explains it to a bug in a renderer would be the worst possible moment.
    /// </remarks>
    private static bool TryCustomFormat(StringBuilder sb, object value, int depth, HashSet<object> seen, FormattingOptions options)
    {
        var scoped = AssertionScope.Current?.ValueFormatters;
        if (scoped is not null && TryFormatWith(scoped, sb, value, depth, seen, options)) return true;

        if (globalFormatters.Count == 0) return false;

        IValueFormatter[] snapshot;
        lock (registrationLock) snapshot = [.. globalFormatters];
        return TryFormatWith(snapshot, sb, value, depth, seen, options);
    }

    private static bool TryFormatWith(IReadOnlyList<IValueFormatter> formatters, StringBuilder sb,
        object value, int depth, HashSet<object> seen, FormattingOptions options)
    {
        for (var i = 0; i < formatters.Count; i++)
        {
            var formatter = formatters[i];
            try
            {
                if (!formatter.CanFormat(value)) continue;
                sb.Append(formatter.Format(value, child =>
                {
                    var nested = new StringBuilder();
                    FormatInto(nested, child, depth + 1, seen, options);
                    return nested.ToString();
                }));
                return true;
            }
            catch
            {
                // Fall through to the next formatter, and ultimately to the built-in rendering.
            }
        }
        return false;
    }

    private static void AppendQuoted(StringBuilder sb, string s, FormattingOptions options)
    {
        sb.Append('"');
        var truncated = s.Length > options.MaxStringLength;
        var text = truncated ? s[..options.MaxStringLength] : s;
        sb.Append(text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
        sb.Append('"');
        if (truncated)
            sb.Append("… (").Append(s.Length - options.MaxStringLength).Append(" more chars)");
    }

    /// <summary>The separator between members or items, and the indent that follows it.</summary>
    private static void AppendSeparator(StringBuilder sb, int depth, FormattingOptions options, bool first)
    {
        if (!options.UseLineBreaks)
        {
            if (!first) sb.Append(", ");
            return;
        }

        if (!first) sb.Append(',');
        sb.Append(Environment.NewLine);
        sb.Append(' ', (depth + 1) * 2);
    }

    private static void AppendClosingIndent(StringBuilder sb, int depth, FormattingOptions options, bool any)
    {
        if (!options.UseLineBreaks || !any) return;
        sb.Append(Environment.NewLine).Append(' ', depth * 2);
    }

    private static void FormatDictionary(StringBuilder sb, IDictionary dictionary, int depth, HashSet<object> seen, FormattingOptions options)
    {
        sb.Append('{');
        var count = 0;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (count >= options.MaxEnumerableItems)
            {
                AppendSeparator(sb, depth, options, first: false);
                sb.Append("… (").Append(dictionary.Count - count).Append(" more)");
                break;
            }
            AppendSeparator(sb, depth, options, first: count == 0);
            sb.Append('[');
            FormatInto(sb, entry.Key, depth + 1, seen, options);
            sb.Append("] = ");
            FormatInto(sb, entry.Value, depth + 1, seen, options);
            count++;
        }
        if (count == 0) sb.Append("empty");
        AppendClosingIndent(sb, depth, options, any: count > 0);
        sb.Append('}');
    }

    private static void FormatEnumerable(StringBuilder sb, IEnumerable enumerable, int depth, HashSet<object> seen, FormattingOptions options)
    {
        sb.Append('{');
        var count = 0;
        var truncated = false;
        foreach (var item in enumerable)
        {
            if (count >= options.MaxEnumerableItems) { truncated = true; break; }
            AppendSeparator(sb, depth, options, first: count == 0);
            FormatInto(sb, item, depth + 1, seen, options);
            count++;
        }
        if (count == 0) sb.Append("empty");
        if (truncated) sb.Append('…');
        AppendClosingIndent(sb, depth, options, any: count > 0);
        sb.Append('}');
    }

    private static void FormatMembers(StringBuilder sb, object value, Type type, int depth, HashSet<object> seen, FormattingOptions options)
    {
        sb.Append(type.Name).Append(" {");
        if (!options.UseLineBreaks) sb.Append(' ');

        var written = 0;
        if (EquivalencyRegistry.TryGetAccessors(type, out var accessors))
        {
            for (var i = 0; i < accessors.Count; i++)
            {
                AppendSeparator(sb, depth, options, first: written == 0);
                sb.Append(accessors[i].Name).Append(" = ");
                FormatInto(sb, GetSafe(() => accessors[i].Getter(value)), depth + 1, seen, options);
                written++;
            }
        }
        else
        {
            var properties = GetPropertiesBestEffort(type);
            for (var i = 0; i < properties.Length; i++)
            {
                AppendSeparator(sb, depth, options, first: written == 0);
                sb.Append(properties[i].Name).Append(" = ");
                FormatInto(sb, GetSafe(() => properties[i].GetValue(value)), depth + 1, seen, options);
                written++;
            }
        }

        AppendClosingIndent(sb, depth, options, any: written > 0);
        if (!options.UseLineBreaks) sb.Append(' ');
        sb.Append('}');
    }

    private static object? GetSafe(Func<object?> getter)
    {
        try { return getter(); }
        catch (Exception ex) { return $"<threw {ex.GetType().Name}>"; }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Best-effort failure-message rendering only; members removed by trimming merely reduce message detail. Generated accessors from EquivalencyRegistry are preferred and trim-safe.")]
    private static PropertyInfo[] GetPropertiesBestEffort(Type type)
    {
        try
        {
            return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)];
        }
        catch
        {
            return [];
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Best-effort failure-message rendering only. If ToString() was trimmed away, the value renders member-by-member instead — message detail changes, correctness does not.")]
    private static bool OverridesToString(Type type)
    {
        try
        {
            var method = type.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            return method is not null && method.DeclaringType != typeof(object) && method.DeclaringType != typeof(ValueType);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNumeric(object value) => value
        is sbyte or byte or short or ushort or int or uint or long or ulong
        or float or double or decimal
        or System.Numerics.BigInteger or Half or Int128 or UInt128 or nint or nuint;
}
