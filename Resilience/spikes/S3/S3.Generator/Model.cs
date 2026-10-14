using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace Spike.S3.Generator
{
    internal static class Outcomes
    {
        /// <summary>Closure-free rewrite exists and is safe; an interceptor is emitted.</summary>
        public const string Lowered = "Lowered";
        /// <summary>Not intercepted: rewriting would change behaviour or is not supported.</summary>
        public const string Fallback = "Fallback";
        /// <summary>Not intercepted: nothing to lower (already 0 B, or not a lambda).</summary>
        public const string NotNeeded = "NotNeeded";
        /// <summary>Not intercepted: the enclosing method has [NoIntercept] (measurement control).</summary>
        public const string Suppressed = "Suppressed";
    }

    /// <summary>
    /// The per-call-site model. Only strings and ints: it must be value-equatable, or the incremental
    /// pipeline re-runs the output step on every keystroke. <c>InterceptableLocation</c> itself is not
    /// stored; its <c>Version</c>/<c>Data</c> are.
    /// </summary>
    internal sealed record SiteModel(
        string Site,
        string DisplayLocation,
        string Outcome,
        string Detail,
        string Rewrite,
        int LocationVersion,
        string LocationData);

    /// <summary>Structural-equality wrapper for ImmutableArray (which compares by reference).</summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        private readonly ImmutableArray<T> _items;
        public EquatableArray(ImmutableArray<T> items) => _items = items;
        public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;
        public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);
        public override bool Equals(object? obj) => obj is EquatableArray<T> o && Equals(o);
        public override int GetHashCode()
        {
            var h = 17;
            foreach (var i in Items) h = unchecked(h * 31 + (i?.GetHashCode() ?? 0));
            return h;
        }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
