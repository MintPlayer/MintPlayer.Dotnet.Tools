using System.Collections;
using System.Collections.Concurrent;
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
/// Rendering is reached only after an assertion has already failed, so everything here is free to a
/// passing suite. That is the reason the custom-formatter lookup, the line-break handling and the
/// options reads can all sit on this path without measurement.
/// </remarks>
public static class Formatter
{
    /// <summary>
    /// Custom renderings, keyed by the exact type they were registered for.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Explicitly registered, never discovered.</b> Scanning loaded assemblies for formatter
    /// types — which is how other libraries do this — is precisely the pattern that breaks under
    /// trimming and AOT, and it would undo the property this library is built around. If a type has
    /// no registration, it renders structurally; it is never searched for.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, Func<object, string>> customFormatters = new();

    /// <summary>
    /// Fast path: the overwhelming majority of runs register nothing, and this keeps them out of the
    /// dictionary and the base-type walk entirely.
    /// </summary>
    private static volatile bool hasCustomFormatters;

    /// <summary>
    /// Registers how values of <typeparamref name="T"/> are rendered in failure messages. Replaces
    /// any previous registration for the same type.
    /// </summary>
    /// <remarks>
    /// Matching is by exact runtime type first, then up the base-class chain. <b>Interfaces are not
    /// matched</b>: a value implementing two registered interfaces has no defensible winner, and a
    /// rule nobody can predict is worse than one more registration.
    /// </remarks>
    public static void Register<T>(Func<T, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        customFormatters[typeof(T)] = value => formatter((T)value);
        hasCustomFormatters = true;
    }

    /// <summary>Removes the registration for <typeparamref name="T"/>. True when one was present.</summary>
    public static bool Unregister<T>()
    {
        var removed = customFormatters.TryRemove(typeof(T), out _);
        if (customFormatters.IsEmpty) hasCustomFormatters = false;
        return removed;
    }

    /// <summary>Removes every custom registration. Intended for a test fixture that added one.</summary>
    public static void ClearCustomFormatters()
    {
        customFormatters.Clear();
        hasCustomFormatters = false;
    }

    public static string Format(object? value)
    {
        var sb = new StringBuilder();
        FormatInto(sb, value, 0, new State());
        return sb.ToString();
    }

    /// <summary>Per-render state: the cycle guard and the line budget.</summary>
    private sealed class State
    {
        public readonly HashSet<object> Seen = [];
        public int Lines = 1;
        public bool Truncated;

        /// <summary>False once the line budget is spent, at which point callers stop emitting.</summary>
        public bool TryTakeLine()
        {
            var max = FormattingOptions.MaxLines;
            if (max > 0 && Lines >= max)
            {
                Truncated = true;
                return false;
            }
            Lines++;
            return true;
        }
    }

    private static void FormatInto(StringBuilder sb, object? value, int depth, State state)
    {
        if (value is not null && hasCustomFormatters && TryFormatCustom(sb, value)) return;

        switch (value)
        {
            case null:
                sb.Append("<null>");
                return;
            case string s:
                AppendQuoted(sb, s);
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
            if (!state.Seen.Add(value))
            {
                sb.Append("{Cyclic reference to ").Append(runtimeType.Name).Append('}');
                return;
            }
            if (depth > FormattingOptions.MaxDepth)
            {
                // Naming the knob is the point. "Type {…}" tells the reader their message was cut
                // short but not that they can do anything about it, so the next step is to reproduce
                // the failure under a debugger — which is exactly the work a good message avoids.
                sb.Append(runtimeType.Name)
                  .Append(" {… depth ").Append(FormattingOptions.MaxDepth)
                  .Append(" reached; raise FormattingOptions.MaxDepth to see more}");
                state.Seen.Remove(value);
                return;
            }
        }

        try
        {
            if (value is IDictionary dictionary)
            {
                FormatDictionary(sb, dictionary, depth, state);
            }
            else if (value is IEnumerable enumerable)
            {
                FormatEnumerable(sb, enumerable, depth, state);
            }
            else if (OverridesToString(runtimeType))
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else
            {
                FormatMembers(sb, value, runtimeType, depth, state);
            }
        }
        finally
        {
            if (!runtimeType.IsValueType)
                state.Seen.Remove(value);
        }
    }

    /// <summary>Exact type first, then up the base chain. See <see cref="Register{T}"/>.</summary>
    private static bool TryFormatCustom(StringBuilder sb, object value)
    {
        for (var type = value.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            if (!customFormatters.TryGetValue(type, out var formatter)) continue;

            // A formatter is user code on the failure path. If it throws, the assertion's real
            // message still has to arrive — swallowing the failure that was being reported because
            // the renderer crashed would be the worst possible outcome here.
            try
            {
                sb.Append(formatter(value));
            }
            catch (Exception ex)
            {
                sb.Append("<custom formatter for ").Append(type.Name)
                  .Append(" threw ").Append(ex.GetType().Name).Append('>');
            }
            return true;
        }

        return false;
    }

    private static void AppendQuoted(StringBuilder sb, string s)
    {
        var limit = FormattingOptions.MaxStringLength;
        sb.Append('"');
        var truncated = s.Length > limit;
        var text = truncated ? s[..limit] : s;
        sb.Append(text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
        sb.Append('"');
        if (truncated)
            sb.Append("… (").Append(s.Length - limit).Append(" more chars)");
    }

    /// <summary>
    /// Writes the separator between two entries: ", " normally, a newline and indent when
    /// <see cref="FormattingOptions.UseLineBreaks"/> is on. False when the line budget is spent.
    /// </summary>
    private static bool AppendSeparator(StringBuilder sb, State state, int depth, bool first)
    {
        if (!FormattingOptions.UseLineBreaks)
        {
            if (!first) sb.Append(", ");
            return true;
        }

        if (!state.TryTakeLine()) return false;

        sb.Append(first ? string.Empty : ",").AppendLine();
        sb.Append(' ', (depth + 1) * 4);
        return true;
    }

    private static void AppendClose(StringBuilder sb, State state, int depth, char close, bool wroteAny)
    {
        if (FormattingOptions.UseLineBreaks && wroteAny)
        {
            sb.AppendLine();
            sb.Append(' ', depth * 4);
        }
        sb.Append(close);
    }

    private static void FormatDictionary(StringBuilder sb, IDictionary dictionary, int depth, State state)
    {
        sb.Append('{');
        var count = 0;
        var elided = false;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (count >= FormattingOptions.MaxCollectionItems) { elided = true; break; }
            if (!AppendSeparator(sb, state, depth, count == 0)) { elided = true; break; }
            sb.Append('[');
            FormatInto(sb, entry.Key, depth + 1, state);
            sb.Append("] = ");
            FormatInto(sb, entry.Value, depth + 1, state);
            count++;
        }
        if (count == 0) sb.Append("empty");
        if (elided) AppendElision(sb, state, dictionary.Count - count);
        AppendClose(sb, state, depth, '}', count > 0);
    }

    private static void FormatEnumerable(StringBuilder sb, IEnumerable enumerable, int depth, State state)
    {
        sb.Append('{');
        var count = 0;
        var elided = false;
        foreach (var item in enumerable)
        {
            if (count >= FormattingOptions.MaxCollectionItems) { elided = true; break; }
            if (!AppendSeparator(sb, state, depth, count == 0)) { elided = true; break; }
            FormatInto(sb, item, depth + 1, state);
            count++;
        }
        if (count == 0) sb.Append("empty");
        if (elided) AppendElision(sb, state, -1);
        AppendClose(sb, state, depth, '}', count > 0);
    }

    /// <summary>
    /// The elision marker names whichever limit actually stopped the rendering, so the reader can
    /// raise the right one instead of guessing between two.
    /// </summary>
    private static void AppendElision(StringBuilder sb, State state, int remaining)
    {
        if (state.Truncated)
        {
            sb.Append("… (truncated at ").Append(FormattingOptions.MaxLines)
              .Append(" lines; raise FormattingOptions.MaxLines)");
            return;
        }

        sb.Append('…');
        if (remaining > 0) sb.Append(" (").Append(remaining).Append(" more)");
        sb.Append(" (raise FormattingOptions.MaxCollectionItems to see more)");
    }

    private static void FormatMembers(StringBuilder sb, object value, Type type, int depth, State state)
    {
        sb.Append(type.Name).Append(" {");
        var wroteAny = false;

        if (EquivalencyRegistry.TryGetAccessors(type, out var accessors))
        {
            for (var i = 0; i < accessors.Length; i++)
            {
                if (!AppendSeparator(sb, state, depth, i == 0)) break;
                if (!FormattingOptions.UseLineBreaks && i == 0) sb.Append(' ');
                sb.Append(accessors[i].Name).Append(" = ");
                FormatInto(sb, GetSafe(value, accessors[i]), depth + 1, state);
                wroteAny = true;
            }
        }
        else
        {
            var properties = GetPropertiesBestEffort(type);
            for (var i = 0; i < properties.Length; i++)
            {
                if (!AppendSeparator(sb, state, depth, i == 0)) break;
                if (!FormattingOptions.UseLineBreaks && i == 0) sb.Append(' ');
                sb.Append(properties[i].Name).Append(" = ");
                FormatInto(sb, GetSafe(value, properties[i]), depth + 1, state);
                wroteAny = true;
            }
        }

        if (!FormattingOptions.UseLineBreaks) sb.Append(' ');
        AppendClose(sb, state, depth, '}', FormattingOptions.UseLineBreaks && wroteAny);
    }

    private static object? GetSafe(object value, MemberAccessor accessor)
    {
        try { return accessor.Getter(value); }
        catch (Exception ex) { return $"<threw {ex.GetType().Name}>"; }
    }

    private static object? GetSafe(object value, PropertyInfo property)
    {
        try { return property.GetValue(value); }
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
