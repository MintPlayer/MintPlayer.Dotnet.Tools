using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections.Immutable;
using System.Threading;
using System.Data;

namespace MintPlayer.SourceGenerators.Tools;

public abstract class IncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var settingsProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (p, ct) =>
            {
                p.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
                return new Settings
                {
                    RootNamespace = rootNamespace,
                };
            })
            .WithComparer(SettingsValueComparer.Instance);

        // IncrementalValueProvider's
        var valueProviders = Setup(context, settingsProvider).ToArray();
        var result = valueProviders.First();

        for (int i = 1; i < valueProviders.Length; i++)
        {
            var current = valueProviders[i];

            //// COMBINE
            Type type1 = result.GetType().GenericTypeArguments[0], type2 = current.GetType().GenericTypeArguments[0];
            var gen = CombineValue.MakeGenericMethod(type1, type2);
            var a = gen.Invoke(null, [result, current]);

            //var tupleCreate = typeof(Tuple)
            //    .GetOverload(
            //        nameof(Tuple.Create),
            //        BindingFlags.Public | BindingFlags.Static,
            //        types => types.Count == 2)!
            //    .MakeGenericMethod(type1, type2);

            //// SELECTMANY
            var tupleType = typeof(ValueTuple<,>).MakeGenericType(type1, type2);
            //var tupleType = typeof(ValueTuple<Producer, Producer>);
            var selectManyMethod = SelectManyValueIEnumerable.MakeGenericMethod(typeof((Producer, Producer)), typeof(Producer));
            try
            {
                var methodToInvoke = i == 1
                        ? mergeOneMethod.MakeGenericMethod(type1, type2)
                        : mergeSeriesMethod.MakeGenericMethod(type2);
                selectManyMethod.Invoke(null, [
                    a,
                    //Delegate.CreateDelegate(typeof(Func<IncrementalValuesProvider<Producer>>), methodToInvoke),
                    //methodToInvoke,
                    new Func<(Producer Left, Producer Right), CancellationToken, Producer[]>(((Producer Left, Producer Right) tuple, CancellationToken token) => [tuple.Left, tuple.Right]),
                ]);
                //selectManyMethod.Invoke(null, [
                //    a,
                //    new Func<(Producer Left, Producer Right), CancellationToken, Producer[]>(((Producer Left, Producer Right) tuple, CancellationToken token) => [tuple.Left, tuple.Right]),
                //]);
            }
            catch (Exception ex)
            {
                throw;
            }

            

            // MakeGenericMethod((type1, type2), typeof(Producer))
            var b = SelectManyValueIEnumerable

                .Invoke(null, [
                    a,
                    static ((Producer Left, Producer Right) producers, CancellationToken ct) => new Producer[] { producers.Left, producers.Right }]
                );

            if (i != valueProviders.Length - 1)
            {
                result = CollectMethod.Invoke(null, [b]);
            }
            else
            {
                result = b;
            }
        }

        //// Combine all Source Providers
        //var sourceProvider = classNamesSourceProvider
        //    .Combine(classNameListSourceProvider)
        //    .SelectMany(static (p, _) => new Producer[] { p.Left, p.Right });

        //// Generate Code
        //context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
    }

    static MethodInfo mergeOneMethod;
    static MethodInfo mergeSeriesMethod;

    static IncrementalGenerator()
    {
        mergeOneMethod = typeof(IncrementalGenerator).GetMethod(nameof(MergeOne), BindingFlags.NonPublic | BindingFlags.Static);
        mergeSeriesMethod = typeof(IncrementalGenerator).GetMethod(nameof(MergeSeries), BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static IEnumerable<Producer> MergeSeries<TRight>((ImmutableArray<Producer> Left, TRight Right) tuple, CancellationToken token)
        where TRight : Producer
    {
        return [.. tuple.Left, tuple.Right];
    }

    private static IEnumerable<Producer> MergeOne<TLeft, TRight>((TLeft Left, TRight Right) tuple, CancellationToken ct)
        where TLeft : Producer
        where TRight : Producer
    {
        return [tuple.Left, tuple.Right];
    }


    //private static Producer[] Merger<T1, T2>((T1 Left, T2 Right) producers, CancellationToken ct)
    //    where T1 : IncrementalValueProvider<T1>
    //    where T2 : Producer
    //{
    //    return new Producer[] { producers.Left, producers.Right };
    //}

    /// <summary>Returns list of <see cref="IncrementalValueProvider{TValue}"/></summary>
    public abstract IEnumerable<object> Setup(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);

    //private static readonly MethodInfo MergerMethod =
    //    typeof(IncrementalGenerator)
    //        .GetMethod(
    //            nameof(Merger),
    //            BindingFlags.NonPublic | BindingFlags.Static);

    protected static readonly MethodInfo CombineValue =
        typeof(IncrementalValueProviderExtensions)
            .GetOverload(
                nameof(IncrementalValueProviderExtensions.Combine),
                BindingFlags.Public | BindingFlags.Static,
                typeof(IncrementalValueProvider<>), typeof(IncrementalValueProvider<>));

    protected static readonly MethodInfo CombineValues =
        typeof(IncrementalValueProviderExtensions)
            .GetOverload(
                nameof(IncrementalValueProviderExtensions.Combine),
                BindingFlags.Public | BindingFlags.Static,
                typeof(IncrementalValuesProvider<>), typeof(IncrementalValueProvider<>));

    protected static readonly MethodInfo SelectManyValueImmutableArray =
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValueProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(ImmutableArray<>)
        );

    protected static readonly MethodInfo SelectManyValueIEnumerable =
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValueProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(IEnumerable<>)
        );

    protected static readonly MethodInfo SelectManyValuesImmutableArray =
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValuesProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(ImmutableArray<>)
        );

    protected static readonly MethodInfo SelectManyValuesIEnumerable =
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValuesProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(IEnumerable<>)
        );

    protected static readonly MethodInfo CollectMethod =
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.Collect),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes => paramTypes[0].BasicType == typeof(IncrementalValuesProvider<>)
        );
}

//public abstract class IncrementalGenerator<T1> : IncrementalGenerator
//    where T1 : Producer
//{
//    internal override IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
//    {
//        var prov = Initialize(context, settingsProvider);
//        return [prov];
//    }

//    // Cannot be changed
//    public abstract IncrementalValueProvider<T1> Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
//}

//public abstract class IncrementalGenerator<T1, T2> : IncrementalGenerator
//    where T1 : Producer
//    where T2 : Producer
//{
//    internal override IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
//    {
//        var provs = Initialize(context, settingsProvider);
//        return provs.Flatten();
//    }

//    // Cannot be changed
//    public abstract (IncrementalValueProvider<T1>, IncrementalValueProvider<T2>) Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
//}

//public abstract class IncrementalGenerator<T1, T2, T3> : IncrementalGenerator
//    where T1 : Producer
//    where T2 : Producer
//    where T3 : Producer
//{
//    internal override IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
//    {
//        var provs = Initialize(context, settingsProvider);
//        return provs.Flatten();
//    }

//    // Cannot be changed
//    public abstract (IncrementalValueProvider<T1>, IncrementalValueProvider<T2>, IncrementalValueProvider<T3>) Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
//}