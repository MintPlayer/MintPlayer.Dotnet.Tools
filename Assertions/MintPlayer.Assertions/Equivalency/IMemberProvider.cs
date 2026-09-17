namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Resolves the comparable members of a type for the equivalency engine. Implementations decide
/// where the accessors come from (source-generated registry, reflection, ...).
/// </summary>
internal interface IMemberProvider
{
    /// <summary>The readable members of <paramref name="type"/>, in declaration order when known.</summary>
    /// <remarks>
    /// ⚠️ <b>The array is the contract. Do not "tidy" this to <c>IReadOnlyList&lt;MemberAccessor&gt;</c>.</b>
    /// <para>
    /// It was that interface, and it cost <b>5.46 KB per comparison — 27% of the whole walk</b>.
    /// <c>foreach</c> over an interface-typed collection calls
    /// <c>IEnumerable&lt;T&gt;.GetEnumerator()</c>, whose return type is the interface, so the
    /// underlying struct enumerator is <b>boxed</b>: one heap allocation per call. The walker looks
    /// up a member by name once per expectation member per node — 112 times on the benchmark graph
    /// alone — so the boxes dominated.
    /// </para>
    /// <para>
    /// Measured on the 5-type/4-level/20-item benchmark graph (net11.0):
    /// <c>IReadOnlyList&lt;T&gt;</c> 20.34 KB/op → <c>MemberAccessor[]</c> <b>14.88 KB/op</b>.
    /// </para>
    /// <para>
    /// The trap is that a boxing <c>foreach</c> is character-for-character identical to an
    /// allocation-free one — only the static type decides — so neither review nor a code search
    /// finds it. <c>foreach</c> over an array compiles to an indexed loop with no enumerator at all.
    /// An indexed loop over the <i>interface</i> is NOT the fix: it removes the allocation and runs
    /// ~1.4× slower than the boxed loop it replaces, because every indexer call and <c>Count</c>
    /// read is an un-inlinable interface dispatch.
    /// </para>
    /// <para>
    /// Nothing is lost by the array: the generator already emits <c>new MemberAccessor[] { … }</c>,
    /// so the interface was only ever narrowing something that was an array all along.
    /// </para>
    /// </remarks>
    MemberAccessor[] GetMembers(Type type);
}
