using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections.Immutable;
using System.Threading;

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

        var valueProviders = InitializeBase(context, settingsProvider).ToArray();
        var result = valueProviders.First();

        for (int i = 1; i < valueProviders.Length; i++)
        {
            var current = valueProviders[i];
            var a = CombineValue.Value.Invoke(null, [result, current]);
            var b = SelectManyValueIEnumerable.Value.Invoke(null, [
                a,
                static ((Producer Left, Producer Right) producers, CancellationToken ct) => new Producer[] { producers.Left, producers.Right }]
            );

            if (i !=  valueProviders.Length - 1)
            {
                result = CollectMethod.Value.Invoke(null, [b]);
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

    internal abstract IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);

    protected static readonly Lazy<MethodInfo> CombineValue = new(() =>
        typeof(IncrementalValueProviderExtensions)
            .GetOverload(
                nameof(IncrementalValueProviderExtensions.Combine),
                BindingFlags.Public | BindingFlags.Static,
                typeof(IncrementalValueProvider<>), typeof(IncrementalValueProvider<>)));

    protected static readonly Lazy<MethodInfo> CombineValues = new(() =>
        typeof(IncrementalValueProviderExtensions)
            .GetOverload(
                nameof(IncrementalValueProviderExtensions.Combine),
                BindingFlags.Public | BindingFlags.Static,
                typeof(IncrementalValuesProvider<>), typeof(IncrementalValueProvider<>)));

    protected static readonly Lazy<MethodInfo> SelectManyValueImmutableArray = new(() =>
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValueProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(ImmutableArray<>)
        )
    );

    protected static readonly Lazy<MethodInfo> SelectManyValueIEnumerable = new(() =>
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValueProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(IEnumerable<>)
        )
    );

    protected static readonly Lazy<MethodInfo> SelectManyValuesImmutableArray = new(() =>
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValuesProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(ImmutableArray<>)
        )
    );

    protected static readonly Lazy<MethodInfo> SelectManyValuesIEnumerable = new(() =>
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.SelectMany),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes =>
                paramTypes[0].BasicType == typeof(IncrementalValuesProvider<>)
                    && paramTypes[1].BasicType == typeof(Func<,,>)
                    && paramTypes[1].GenericArguments[2].GetGenericTypeDefinition() == typeof(IEnumerable<>)
        )
    );

    protected static readonly Lazy<MethodInfo> CollectMethod = new(() =>
        typeof(IncrementalValueProviderExtensions).GetOverload(
            nameof(IncrementalValueProviderExtensions.Collect),
            BindingFlags.Public | BindingFlags.Static,
            paramTypes => paramTypes[0].BasicType == typeof(IncrementalValuesProvider<>)
        )
    );
}

public abstract class IncrementalGenerator<T1> : IncrementalGenerator
    where T1 : Producer
{
    internal override IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var prov = Initialize(context, settingsProvider);
        return [prov];
    }

    // Cannot be changed
    public abstract IncrementalValueProvider<T1> Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}

public abstract class IncrementalGenerator<T1, T2> : IncrementalGenerator
    where T1 : Producer
    where T2 : Producer
{
    internal override IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var provs = Initialize(context, settingsProvider);
        return provs.Flatten();
    }

    // Cannot be changed
    public abstract (IncrementalValueProvider<T1>, IncrementalValueProvider<T2>) Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}

public abstract class IncrementalGenerator<T1, T2, T3> : IncrementalGenerator
    where T1 : Producer
    where T2 : Producer
    where T3 : Producer
{
    internal override IEnumerable<object> InitializeBase(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var provs = Initialize(context, settingsProvider);
        return provs.Flatten();
    }

    // Cannot be changed
    public abstract (IncrementalValueProvider<T1>, IncrementalValueProvider<T2>, IncrementalValueProvider<T3>) Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider);
}