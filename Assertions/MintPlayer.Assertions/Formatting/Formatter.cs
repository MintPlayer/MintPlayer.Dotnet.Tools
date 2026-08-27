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
public static class Formatter
{
    private const int MaxStringLength = 512;
    private const int MaxEnumerableItems = 32;
    private const int MaxDepth = 3;

    public static string Format(object? value)
    {
        var sb = new StringBuilder();
        FormatInto(sb, value, 0, []);
        return sb.ToString();
    }

    private static void FormatInto(StringBuilder sb, object? value, int depth, HashSet<object> seen)
    {
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
            if (!seen.Add(value))
            {
                sb.Append("{Cyclic reference to ").Append(runtimeType.Name).Append('}');
                return;
            }
            if (depth > MaxDepth)
            {
                sb.Append(runtimeType.Name).Append(" {…}");
                seen.Remove(value);
                return;
            }
        }

        try
        {
            if (value is IDictionary dictionary)
            {
                FormatDictionary(sb, dictionary, depth, seen);
            }
            else if (value is IEnumerable enumerable)
            {
                FormatEnumerable(sb, enumerable, depth, seen);
            }
            else if (OverridesToString(runtimeType))
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else
            {
                FormatMembers(sb, value, runtimeType, depth, seen);
            }
        }
        finally
        {
            if (!runtimeType.IsValueType)
                seen.Remove(value);
        }
    }

    private static void AppendQuoted(StringBuilder sb, string s)
    {
        sb.Append('"');
        var truncated = s.Length > MaxStringLength;
        var text = truncated ? s[..MaxStringLength] : s;
        sb.Append(text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
        sb.Append('"');
        if (truncated)
            sb.Append("… (").Append(s.Length - MaxStringLength).Append(" more chars)");
    }

    private static void FormatDictionary(StringBuilder sb, IDictionary dictionary, int depth, HashSet<object> seen)
    {
        sb.Append('{');
        var count = 0;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (count > 0) sb.Append(", ");
            if (count >= MaxEnumerableItems) { sb.Append("… (").Append(dictionary.Count - count).Append(" more)"); break; }
            sb.Append('[');
            FormatInto(sb, entry.Key, depth + 1, seen);
            sb.Append("] = ");
            FormatInto(sb, entry.Value, depth + 1, seen);
            count++;
        }
        if (count == 0) sb.Append("empty");
        sb.Append('}');
    }

    private static void FormatEnumerable(StringBuilder sb, IEnumerable enumerable, int depth, HashSet<object> seen)
    {
        sb.Append('{');
        var count = 0;
        var truncated = false;
        foreach (var item in enumerable)
        {
            if (count > 0) sb.Append(", ");
            if (count >= MaxEnumerableItems) { truncated = true; break; }
            FormatInto(sb, item, depth + 1, seen);
            count++;
        }
        if (count == 0) sb.Append("empty");
        if (truncated) sb.Append('…');
        sb.Append('}');
    }

    private static void FormatMembers(StringBuilder sb, object value, Type type, int depth, HashSet<object> seen)
    {
        sb.Append(type.Name).Append(" { ");

        if (EquivalencyRegistry.TryGetAccessors(type, out var accessors))
        {
            for (var i = 0; i < accessors.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(accessors[i].Name).Append(" = ");
                FormatInto(sb, GetSafe(() => accessors[i].Getter(value)), depth + 1, seen);
            }
        }
        else
        {
            var properties = GetPropertiesBestEffort(type);
            for (var i = 0; i < properties.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(properties[i].Name).Append(" = ");
                FormatInto(sb, GetSafe(() => properties[i].GetValue(value)), depth + 1, seen);
            }
        }

        sb.Append(" }");
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
