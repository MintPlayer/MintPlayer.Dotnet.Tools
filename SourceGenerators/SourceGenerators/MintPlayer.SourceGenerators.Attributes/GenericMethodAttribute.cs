namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Generates public generic overloads <c>M&lt;T1, T2&gt;(T1 t1, T2 t2)</c> up to <c>M&lt;T1, ..., Tn&gt;</c> for a private
/// method that takes one collection of <see cref="object"/>. Each overload passes its arguments to the method as one
/// collection expression and returns its result.
/// </summary>
/// <remarks>
/// <para>
/// The decorated method must be <c>private</c>, take exactly one parameter that a collection expression can build
/// from <see cref="object"/> elements (<c>object[]</c>, <c>params object[]</c>, <c>IEnumerable&lt;object&gt;</c>,
/// <c>IReadOnlyList&lt;object&gt;</c>, <c>List&lt;object&gt;</c>, <c>ReadOnlySpan&lt;object&gt;</c>, ...), must not be
/// generic, and its type and every containing type must be <c>partial</c>.
/// </para>
/// <para>
/// Overloads start at two arguments. A one-argument overload would hijack every call that passes a collection,
/// <c>Add(list)</c> included, and wrap it in a one-element collection.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GenericMethodAttribute : Attribute
{
    /// <summary>Generates overloads with 2 up to <paramref name="count"/> arguments.</summary>
    /// <param name="count">The highest number of arguments, from 2 to 16.</param>
    public GenericMethodAttribute(int count = 4)
    {
        Count = count;
    }

    /// <summary>The highest number of arguments an overload takes, from 2 to 16. The default is 4.</summary>
    public int Count { get; set; }
}
