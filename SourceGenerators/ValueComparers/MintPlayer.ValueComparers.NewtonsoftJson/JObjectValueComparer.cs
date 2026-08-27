using MintPlayer.SourceGenerators.Tools.Polyfills;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MintPlayer.ValueComparers.NewtonsoftJson;

/// <summary>
/// Value Comparer for JObject types
/// </summary>
public sealed class JObjectValueComparer : ValueComparer<JObject>
{
    protected override bool AreEqual(JObject x, JObject y)
    {
        return IsEquals(x.ToString(Formatting.None), y.ToString(Formatting.None));
    }

    /// <summary>
    /// Hashes the same normalized JSON that <see cref="AreEqual"/> compares. Without this
    /// the base implementation falls through to JObject's own GetHashCode, which is not
    /// structural — so two objects this comparer calls equal produced different hashes,
    /// breaking the IEqualityComparer contract and silently losing entries in any
    /// dictionary or set keyed on it (including the incremental-generator caches this
    /// comparer exists to serve).
    /// </summary>
    protected override void AddHash(ref HashCodeCompat h, JObject? obj)
    {
        h.Add(obj is null ? null : obj.ToString(Formatting.None));
    }

    public static void Register()
    {
        ComparerRegistry.TryRegister(typeof(JObject), new JObjectValueComparer());
    }
}