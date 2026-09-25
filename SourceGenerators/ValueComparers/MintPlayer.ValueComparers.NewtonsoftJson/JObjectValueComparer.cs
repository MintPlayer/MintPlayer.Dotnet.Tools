using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MintPlayer.ValueComparers.NewtonsoftJson;

/// <summary>
/// Compares <see cref="JObject"/>s by their compact serialized form (ordinal), so equal JSON compares equal.
/// </summary>
/// <remarks>
/// Use it on a <c>JObject</c> property of a <c>[GenerateEquality]</c> model:
/// <c>[UseEqualityComparer(typeof(JObjectValueComparer))]</c>. The generated equality then calls
/// <see cref="Instance"/>. Property order is significant, because the serialized form preserves it.
/// </remarks>
public sealed class JObjectValueComparer : IEqualityComparer<JObject?>
{
    public static readonly JObjectValueComparer Instance = new();

    public bool Equals(JObject? x, JObject? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return string.Equals(x.ToString(Formatting.None), y.ToString(Formatting.None), StringComparison.Ordinal);
    }

    /// <summary>
    /// Hashes the same normalized JSON that <see cref="Equals(JObject, JObject)"/> compares. JObject's own
    /// GetHashCode is not structural, so two objects this comparer calls equal would otherwise hash differently,
    /// breaking the <see cref="IEqualityComparer{T}"/> contract.
    /// </summary>
    public int GetHashCode(JObject? obj)
        => obj is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.ToString(Formatting.None));
}
