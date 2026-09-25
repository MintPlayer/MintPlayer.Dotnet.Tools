using System.Collections;
using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// An immutable array with structural equality. Return it from an incremental-pipeline step that builds a new
/// collection (a <c>Select</c> after <c>Collect()</c> that filters or projects): a plain array or
/// <see cref="ImmutableArray{T}"/> compares by reference, so that step would report <c>Modified</c> on every
/// run and defeat caching downstream.
/// </summary>
/// <remarks>A default (<c>default(EquatableArray&lt;T&gt;)</c>) array behaves as empty and equals <see cref="Empty"/>.</remarks>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly T[]? items;

    public EquatableArray(T[]? items) => this.items = items;

    public EquatableArray(IEnumerable<T> items) => this.items = items.ToArray();

    public static readonly EquatableArray<T> Empty = new(System.Array.Empty<T>());

    public int Count => items?.Length ?? 0;

    public bool IsEmpty => Count == 0;

    public T this[int index] => (items ?? System.Array.Empty<T>())[index];

    public ImmutableArray<T> AsImmutableArray() => items is null ? ImmutableArray<T>.Empty : ImmutableArray.Create(items);

    public bool Equals(EquatableArray<T> other)
        => ValueEquality.Array(items ?? System.Array.Empty<T>(), other.items ?? System.Array.Empty<T>());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode() => ValueEquality.ArrayHash(items ?? System.Array.Empty<T>());

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(items ?? System.Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(T[]? items) => new(items);

    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items.IsDefault ? null : items.ToArray());
}

public static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source) => new(source);

    public static EquatableArray<T> ToEquatableArray<T>(this ImmutableArray<T> source) => source;
}
