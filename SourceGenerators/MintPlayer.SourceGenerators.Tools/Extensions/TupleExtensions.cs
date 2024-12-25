using System;
using System.Collections.Generic;
using System.Linq;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

internal static class TupleExtensions
{
    public static IEnumerable<TResult> Flatten<TResult, T1, T2>(this Tuple<T1, T2> tuple)
        where T1 : TResult
        where T2 : TResult
        => Flatten(tuple).Cast<TResult>();

    public static IEnumerable<TResult> Flatten<TResult, T1, T2, T3>(this Tuple<T1, T2, T3> tuple)
        where T1 : TResult
        where T2 : TResult
        where T3 : TResult
        => Flatten(tuple).Cast<TResult>();

    public static IEnumerable<TResult> Flatten<TResult, T1, T2, T3, T4>(this Tuple<T1, T2, T3, T4> tuple)
        where T1 : TResult
        where T2 : TResult
        where T3 : TResult
        where T4 : TResult
        => Flatten(tuple).Cast<TResult>();

    public static IEnumerable<TResult> Flatten<TResult, T1, T2, T3, T4, T5>(this Tuple<T1, T2, T3, T4, T5> tuple)
        where T1 : TResult
        where T2 : TResult
        where T3 : TResult
        where T4 : TResult
        where T5 : TResult
        => Flatten(tuple).Cast<TResult>();

    public static IEnumerable<TResult> Flatten<TResult, T1, T2, T3, T4, T5, T6>(this Tuple<T1, T2, T3, T4, T5, T6> tuple)
        where T1 : TResult
        where T2 : TResult
        where T3 : TResult
        where T4 : TResult
        where T5 : TResult
        where T6 : TResult
        => Flatten(tuple).Cast<TResult>();

    public static IEnumerable<TResult> Flatten<TResult, T1, T2, T3, T4, T5, T6, T7>(this Tuple<T1, T2, T3, T4, T5, T6, T7> tuple)
        where T1 : TResult
        where T2 : TResult
        where T3 : TResult
        where T4 : TResult
        where T5 : TResult
        where T6 : TResult
        where T7 : TResult
        => Flatten(tuple).Cast<TResult>();

    //public static IEnumerable<TResult> Flatten<TResult, T1, T2, T3, T4, T5, T6, T7, TRest>(this Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple)
    //    where T1 : TResult
    //    where T2 : TResult
    //    where T3 : TResult
    //    where T4 : TResult
    //    where T5 : TResult
    //    where T6 : TResult
    //    where T7 : TResult
    //    where TRest : IEnumerable<TResult>
    //    => Flatten(tuple).Cast<TResult>();



    public static IEnumerable<object?> Flatten<T1, T2>(this Tuple<T1, T2> tuple)
        => [tuple.Item1, tuple.Item2];

    public static IEnumerable<object?> Flatten<T1, T2, T3>(this Tuple<T1, T2, T3> tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3];
    
    public static IEnumerable<object?> Flatten<T1, T2, T3, T4>(this Tuple<T1, T2, T3, T4> tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4];
    
    public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5>(this Tuple<T1, T2, T3, T4, T5> tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5];
    
    public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5, T6>(this Tuple<T1, T2, T3, T4, T5, T6> tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6];

    public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5, T6, T7>(this Tuple<T1, T2, T3, T4, T5, T6, T7> tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6, tuple.Item7];

    //public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5, T6, T7, TRest>(this Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple)
    //    where TRest : Tuple
    //    => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6, tuple.Item7, ..tuple.Rest];

    public static IEnumerable<object?> Flatten<T1, T2>(this (T1, T2) tuple)
        => [tuple.Item1, tuple.Item2];

    public static IEnumerable<object?> Flatten<T1, T2, T3>(this (T1, T2, T3) tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3];

    public static IEnumerable<object?> Flatten<T1, T2, T3, T4>(this (T1, T2, T3, T4) tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4];

    public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5>(this (T1, T2, T3, T4, T5) tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5];

    public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5, T6>(this (T1, T2, T3, T4, T5, T6) tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6];

    public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5, T6, T7>(this (T1, T2, T3, T4, T5, T6, T7) tuple)
        => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6, tuple.Item7];

    //public static IEnumerable<object?> Flatten<T1, T2, T3, T4, T5, T6, T7, TRest>(this Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple)
    //    where TRest : Tuple
    //    => [tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6, tuple.Item7, ..tuple.Rest];

}
