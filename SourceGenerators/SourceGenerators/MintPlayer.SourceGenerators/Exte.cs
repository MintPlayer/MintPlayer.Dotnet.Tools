using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators
{
    public static class StaticMethods
    {
        public static IncrementalValuesProvider<string> Combine<T1, T2, T3>((IncrementalValueProvider<T1>, IncrementalValueProvider<T2>, IncrementalValueProvider<T3>) tuple)
        {
            return tuple.Item1
                .Combine(tuple.Item2)
                .SelectMany(static (p, _) => new object[] { p.Left, p.Right })
                .Combine(tuple.Item3)
                .SelectMany(static (p, _) => new object[] { p.Left, p.Right })
                .Select(static (p, ct) =>
                {
                    return "";
                    // Do something with p.Item1, p.Item2, and p.Item3
                });
        }
    }

}
