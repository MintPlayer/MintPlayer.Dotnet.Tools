using System.Text;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// One step of the walker's position in the object graph: a member name (<c>City</c>) or an index
/// or key (<c>[2]</c>, <c>["eur"]</c>).
/// </summary>
/// <remarks>
/// A struct, and never rendered on its own. See <see cref="PathStack"/> for why the path is not a
/// string any more.
/// </remarks>
internal readonly struct PathSegment(string? name, string? index)
{
    /// <summary>The root step, which contributes nothing to the rendered path.</summary>
    public static PathSegment None => default;

    public static PathSegment ForMember(string memberName) => new(memberName, null);

    public static PathSegment ForIndex(string indexText) => new(null, indexText);

    public bool IsNone => name is null && index is null;

    internal void AppendTo(StringBuilder sb)
    {
        if (index is not null)
        {
            sb.Append('[').Append(index).Append(']');
            return;
        }

        if (name is null) return;
        if (sb.Length > 0) sb.Append('.');
        sb.Append(name);
    }
}

/// <summary>
/// Where the walker currently is, as a push/pop stack of <see cref="PathSegment"/> rather than a
/// string that is rebuilt at every step. Renders to the familiar dotted form —
/// <c>Lines[2].Product.Name</c> — only when something actually asks for the text.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This exists because the path string was 49% of the entire comparison.</b> The walker used
/// to build <c>$"{path}.{member}"</c> once per member per node — 112 strings on the benchmark graph
/// — and then throw every one away, because a passing comparison reports no differences and so never
/// reads a path. Measured by deleting the concatenation: <b>13,992 → 7,144 B/op</b>. Reintroducing a
/// string parameter threaded through the walk gives all of that back.
/// </para>
/// <para>
/// ⚠️ <b>Push and pop must stay balanced, and the <c>finally</c> in <c>CompareNode</c> is what
/// guarantees it.</b> A custom comparer throwing, a formatter throwing, an early <c>return</c> added
/// to the middle of the walk — any of those unbalances the stack, and the symptom is not a crash but
/// silently wrong paths in later failure messages. If you add a return path to <c>CompareNode</c>,
/// it goes inside the <c>try</c>.
/// </para>
/// <para>
/// The first design here was a <c>ref struct</c> chained through the recursion by a <c>ref</c>
/// field, which needs no stack at all. C# rejects it: a ref field cannot refer to a ref struct
/// (CS9050). Kept as a note so the next person does not spend the same half hour discovering it.
/// </para>
/// </remarks>
internal sealed class PathStack
{
    // Grows to the graph's depth and is then reused for the whole comparison, so the steady state
    // costs nothing per node. A List<T> of a struct stores the segments inline.
    //
    // ⚠️ Do NOT give this an initial capacity. It looks like an obvious improvement — growing from
    // empty reallocates at 4, then 8, then 16 — and it measures WORSE: new(16) took the walk from
    // 6,808 to 6,848 B/op and the per-comparison fixed cost from 1,168 to 1,360, because a
    // 16-element array of a 16-byte struct is 256 bytes paid up front by every comparison, including
    // the shallow ones that are most of them. The doubling growth is cheaper than the guess.
    private readonly List<PathSegment> segments = [];

    public void Push(in PathSegment segment) => segments.Add(segment);

    public void Pop() => segments.RemoveAt(segments.Count - 1);

    /// <summary>True when nothing but root segments are on the stack.</summary>
    public bool IsRoot
    {
        get
        {
            foreach (var segment in segments)
            {
                if (!segment.IsNone) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The dotted text of the current position. <b>Call this only when the text is actually
    /// needed</b> — reporting a difference, or testing an exclusion that is configured.
    /// </summary>
    public string ToPathString() => Render(PathSegment.None);

    /// <summary>The text the current position would have with one more segment appended.</summary>
    /// <remarks>
    /// For the two checks that need a child's path <em>before</em> descending into it: whether the
    /// child is excluded, and the message when the subject lacks the member entirely. Pushing and
    /// popping around those would work and would read worse.
    /// </remarks>
    public string ToPathStringWith(in PathSegment extra) => Render(extra);

    private string Render(in PathSegment extra)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            segment.AppendTo(sb);
        }
        extra.AppendTo(sb);
        return sb.ToString();
    }
}
