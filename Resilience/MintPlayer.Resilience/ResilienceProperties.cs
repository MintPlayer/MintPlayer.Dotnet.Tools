using System.Diagnostics.CodeAnalysis;

namespace MintPlayer.Resilience;

/// <summary>A typed key for a value in <see cref="ResilienceProperties"/>.</summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
public readonly struct ResiliencePropertyKey<TValue>
{
    /// <summary>Initializes a key.</summary>
    /// <param name="key">The name of the key.</param>
    public ResiliencePropertyKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
    }

    /// <summary>Gets the name of the key.</summary>
    public string Key { get; }

    /// <inheritdoc />
    public override string ToString() => Key;
}

/// <summary>Typed properties attached to a <see cref="ResilienceContext"/>.</summary>
public sealed class ResilienceProperties
{
    private readonly Dictionary<string, object?> _values = [];

    /// <summary>Gets a value.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, when found.</param>
    /// <returns><see langword="true"/> when the key is present and holds a <typeparamref name="TValue"/> (or <see langword="null"/>).</returns>
    public bool TryGetValue<TValue>(ResiliencePropertyKey<TValue> key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_values.TryGetValue(key.Key, out var stored))
        {
            if (stored is TValue typed)
            {
                value = typed;
                return true;
            }

            if (stored is null)
            {
                value = default!;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Gets a value, or <paramref name="defaultValue"/> when it is absent.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The value to return when the key is absent.</param>
    /// <returns>The stored value, or <paramref name="defaultValue"/>.</returns>
    public TValue GetValue<TValue>(ResiliencePropertyKey<TValue> key, TValue defaultValue)
        => TryGetValue(key, out var value) ? value : defaultValue;

    /// <summary>Sets a value.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set<TValue>(ResiliencePropertyKey<TValue> key, TValue value) => _values[key.Key] = value;

    internal void Clear() => _values.Clear();

    /// <summary>Copies every value of <paramref name="other"/> into this instance, replacing existing keys.</summary>
    internal void AddOrReplaceProperties(ResilienceProperties other)
    {
        foreach (var pair in other._values)
        {
            _values[pair.Key] = pair.Value;
        }
    }
}
