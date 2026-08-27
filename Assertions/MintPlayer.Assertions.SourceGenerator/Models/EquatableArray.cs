using System.Collections;

namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>
/// An array wrapper with structural equality, so incremental pipeline models that carry
/// collections keep the caching behaviour Roslyn expects (a plain <c>T[]</c> compares by
/// reference and would defeat every incremental step).
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? items;

    public EquatableArray(T[]? items) => this.items = items;

    public static readonly EquatableArray<T> Empty = new([]);

    public int Count => items?.Length ?? 0;

    public T this[int index] => items![index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = items ?? [];
        var right = other.items ?? [];
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in items ?? [])
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(T[] items) => new(items);
}
