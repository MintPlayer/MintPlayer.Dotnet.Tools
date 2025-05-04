namespace Microsoft.CodeAnalysis;

public static class IncrementalValueProviderEx
{
    public static IncrementalValueProvider<(T1, T2)> Join<T1, T2>(
        this IncrementalValueProvider<T1> first,
        IncrementalValueProvider<T2> second)
    {
        return IncrementalValueProviderExtensions.Combine(first, second);
    }

    public static IncrementalValueProvider<(T1, T2, T3)> Join<T1, T2, T3>(
        this IncrementalValueProvider<(T1, T2)> previous,
        IncrementalValueProvider<T3> third)
    {
        return IncrementalValueProviderExtensions.Combine(previous, third)
            .Select(static (t, _) => (t.Left.Item1, t.Left.Item2, t.Right));
    }

    public static IncrementalValueProvider<(T1, T2, T3, T4)> Join<T1, T2, T3, T4>(
        this IncrementalValueProvider<(T1, T2, T3)> previous,
        IncrementalValueProvider<T4> fourth)
    {
        return IncrementalValueProviderExtensions.Combine(previous, fourth)
            .Select(static (t, _) => (t.Left.Item1, t.Left.Item2, t.Left.Item3, t.Right));
    }

    public static IncrementalValueProvider<(T1, T2, T3, T4, T5)> Join<T1, T2, T3, T4, T5>(
        this IncrementalValueProvider<(T1, T2, T3, T4)> previous,
        IncrementalValueProvider<T5> fifth)
    {
        return IncrementalValueProviderExtensions.Combine(previous, fifth)
            .Select(static (t, _) => (t.Left.Item1, t.Left.Item2, t.Left.Item3, t.Left.Item4, t.Right));
    }

    // ...and so on for more items
}
