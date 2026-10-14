using MintPlayer.Resilience.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

internal sealed partial class PipelineEmitter
{
    private const string Ext = "global::MintPlayer.Resilience.";

    /// <summary>
    /// The runtime snapshot: the clock, the CTS pool, the runtime strategies and (reloadable) the values. One instance is
    /// published at a time; every execution reads it once at entry (S6), so a reload never mixes values within an execution.
    /// </summary>
    private void EmitRuntime()
    {
        L("/// <summary>The clock, shared state and settings of the pipeline; replaced as a whole by UseTimeProvider and TryApply.</summary>");
        using (Open("private sealed class __Runtime"))
        {
            L("public readonly global::System.TimeProvider TimeProvider;");
            L($"public readonly {Func}<double>? Randomizer;");
            L($"public readonly {Func}<double> Random;");
            L($"public readonly {P}CancellationTokenSourcePool Pool;");
            if (_m.Reloadable)
            {
                L($"public readonly {OptionsFull} Options;");
            }

            if (_m.Budget is not null)
            {
                L($"public readonly {Res}Retry.RetryBudget ClassBudget;");
            }

            foreach (var strategy in _m.Strategies)
            {
                var i = I(strategy);
                var spec = StrategySchema.Of(strategy.Kind);
                if (strategy.Kind == StrategyKind.Retry && HasBudget(strategy))
                {
                    L($"public readonly {Res}Retry.RetryBudget? B{i};");
                }

                if (Interpreted)
                {
                    continue;
                }

                if (spec.Inline)
                {
                    if (_m.Reloadable)
                    {
                        foreach (var value in spec.Values)
                        {
                            L($"public readonly {SectionType(value)} {Field(strategy, value.Option)};");
                        }
                    }
                }
                else
                {
                    L(IsGeneric ? $"public readonly {P}GeneratedStrategy S{i};" : $"public readonly {P}GeneratedStrategy<{R}> S{i};");
                }
            }

            if (Interpreted)
            {
                L($"public readonly {Res}ResiliencePipeline<{R}> Pipeline;");
            }

            Blank();
            var owner = _m.IsInstance ? $", {Self} owner" : string.Empty;
            var options = _m.Reloadable ? $", {OptionsFull} options" : string.Empty;
            using (Open($"public __Runtime(global::System.TimeProvider timeProvider, {Func}<double>? randomizer{owner}{options}, __Runtime? previous, bool reuse)"))
            {
                L("TimeProvider = timeProvider;");
                L("Randomizer = randomizer;");
                L("Random = randomizer ?? global::System.Random.Shared.NextDouble;");
                L($"Pool = {P}CancellationTokenSourcePool.For(timeProvider);");
                if (_m.Reloadable)
                {
                    foreach (var strategy in _m.Strategies.Where(s => StrategySchema.Of(s.Kind).Values.Length > 0))
                    {
                        using (Open($"if (options.{strategy.Key} is null)"))
                        {
                            L($"throw new global::System.ComponentModel.DataAnnotations.ValidationException({Str($"The '{strategy.Key}' section of {OptionsName} is missing.")});");
                        }
                    }

                    L("Options = options;");
                }

                if (_m.Budget is { } budget)
                {
                    L("// The pipeline's own budget: its configuration is constant, so it survives a reload (not a new clock).");
                    L($"ClassBudget = reuse && previous is not null ? previous.ClassBudget : new {Res}Retry.RetryBudget({budget.RetryRatio}, {budget.MinRetriesPerSecond}, {TS}.FromMilliseconds({budget.TimeToLiveMs}), timeProvider);");
                }

                foreach (var strategy in _m.Strategies.Where(s => s.Kind == StrategyKind.Retry && HasBudget(s)))
                {
                    L($"B{I(strategy)} = {BudgetExpression(strategy)};");
                }

                if (Interpreted)
                {
                    EmitInterpreterBuild();
                }
                else
                {
                    // Three phases, so that nothing is released before every value has been validated: (1) validate the
                    // inlined strategies and add each runtime strategy to its builder (Add validates); (2) release the
                    // breakers being replaced; (3) build.
                    foreach (var strategy in _m.Strategies)
                    {
                        EmitRuntimeStrategy(strategy);
                    }

                    foreach (var strategy in Delegated().Where(s => s.Kind == StrategyKind.CircuitBreaker))
                    {
                        var i = I(strategy);
                        L(_m.Reloadable
                            ? $"if (builder{i} is not null) previous?.S{i}.Release();"
                            : $"previous?.S{i}.Release();");
                    }

                    foreach (var strategy in Delegated())
                    {
                        var i = I(strategy);
                        var from = IsGeneric ? $"{P}GeneratedStrategy.From" : $"{P}GeneratedStrategy<{R}>.From";
                        L(_m.Reloadable
                            ? $"S{i} = builder{i} is null ? previous!.S{i} : {from}(builder{i}.Build());"
                            : $"S{i} = {from}(builder{i}.Build());");
                    }
                }
            }

            if (_m.Reloadable && !Interpreted)
            {
                foreach (var strategy in Delegated().Where(s => StrategySchema.Of(s.Kind).Values.Length > 0))
                {
                    var spec = StrategySchema.Of(strategy.Kind);
                    var section = $"{OptionsFull}.{spec.DefaultKey}Section";
                    Blank();
                    L($"private static bool __Same{I(strategy)}({section} a, {section} b)");
                    L($"    => {string.Join(" && ", spec.Values.Select(v => $"a.{v.Option} == b.{v.Option}"))};");
                }
            }
        }
    }

    private IEnumerable<StrategyModel> Delegated() => _m.Strategies.Where(s => !StrategySchema.Of(s.Kind).Inline);

    private bool HasBudget(StrategyModel retry) => Member(retry, "Budget") is not null || _m.Budget is not null;

    private string BudgetExpression(StrategyModel retry)
        => Member(retry, "Budget") is { } member ? MemberExpression(member) : "ClassBudget";

    private string MemberExpression(MemberRefModel member) => $"{(member.IsStatic ? Self : "owner")}.{member.Member}";

    /// <summary>Phase 1 for one strategy: validate an inlined one, or create and fill the builder of a runtime one.</summary>
    private void EmitRuntimeStrategy(StrategyModel strategy)
    {
        var i = I(strategy);
        var spec = StrategySchema.Of(strategy.Kind);
        switch (strategy.Kind)
        {
            case StrategyKind.Timeout:
                L($"// [{i}] Timeout: validated with the runtime's rules; the timeout itself is inlined.");
                L($"{Support}.Validate(new {Res}Timeout.TimeoutStrategyOptions {{ Timeout = {BuildValue(strategy, spec.Values[0])} }});");
                EmitInlineFields(strategy, spec);
                return;
            case StrategyKind.Retry:
                L($"// [{i}] Retry: validated with the runtime's rules; the retry loop itself is inlined.");
                var values = string.Join(", ", spec.Values.Select(v => $"{v.Option} = {BuildValue(strategy, v)}"));
                L($"{Support}.Validate(new {Res}Retry.RetryStrategyOptions {{ {values} }});");
                EmitInlineFields(strategy, spec);
                return;
            case StrategyKind.Fallback:
                return;
        }

        L($"// [{i}] {spec.DefaultKey}: the runtime strategy, driven through its own hooks.");
        var builderType = IsGeneric ? $"{Res}ResiliencePipelineBuilder" : $"{Res}ResiliencePipelineBuilder<{R}>";
        var create = $"new {builderType} {{ Name = {Str(_m.PipelineName)}, TimeProvider = timeProvider }}";
        if (_m.Reloadable)
        {
            // A strategy whose section did not change keeps its instance, so its state survives the reload.
            var same = spec.Values.Length > 0 ? $" && __Same{i}(previous.Options.{strategy.Key}, options.{strategy.Key})" : string.Empty;
            L($"{builderType}? builder{i} = null;");
            using (Open($"if (!(reuse && previous is not null{same}))"))
            {
                L($"builder{i} = {create};");
                EmitAdd(strategy, $"builder{i}", IsGeneric ? "object" : R);
            }
        }
        else
        {
            L($"var builder{i} = {create};");
            EmitAdd(strategy, $"builder{i}", IsGeneric ? "object" : R);
        }
    }

    private void EmitInlineFields(StrategyModel strategy, KindSpec spec)
    {
        if (!_m.Reloadable)
        {
            return;
        }

        foreach (var value in spec.Values)
        {
            L($"{Field(strategy, value.Option)} = {BuildValue(strategy, value)};");
        }
    }


    private void EmitInterpreterBuild()
    {
        L("// Hedging runs attempts concurrently on their own frames, so this pipeline is not flattened: the runtime pipeline");
        L("// is built once, with the same strategies in the same order, and every execution member forwards to it.");
        L($"var builder = new {Res}ResiliencePipelineBuilder<{R}> {{ Name = {Str(_m.PipelineName)}, TimeProvider = timeProvider }};");
        foreach (var strategy in _m.Strategies)
        {
            EmitAdd(strategy, "builder", R);
        }

        L("// The pipeline being replaced releases its breakers' attachments first (every value is validated by now).");
        using (Open("if (previous is not null)"))
        {
            L($"{Support}.ReleaseAttachments(previous.Pipeline);");
        }

        L($"Pipeline = {Ext}ResiliencePipelineBuilderExtensions.UsePooledAsync(builder, {(_m.PooledAsync ? "true" : "false")}).Build();");
    }

    /// <summary>Adds one strategy to a runtime builder, its options built from the attribute values (or the reloaded options).</summary>
    /// <param name="strategy">The strategy.</param>
    /// <param name="builder">The builder variable.</param>
    /// <param name="r">The result type of typed options and hook arguments: the pipeline's, or <c>object</c> for a generic pipeline.</param>
    private void EmitAdd(StrategyModel strategy, string builder, string r)
    {
        var spec = StrategySchema.Of(strategy.Kind);
        var typed = r != "object";
        var props = new List<string>();
        if (strategy.Name is not null)
        {
            props.Add($"Name = {Str(strategy.Name)}");
        }

        foreach (var value in spec.Values)
        {
            props.Add($"{value.Option} = {BuildValue(strategy, value)}");
        }

        foreach (var hookSpec in spec.Hooks)
        {
            if (Hook(strategy, hookSpec.Slot) is { } hook && !(strategy.Kind == StrategyKind.RateLimiter && hookSpec.Slot == "RateLimiter"))
            {
                props.Add($"{hookSpec.Slot} = {Lambda(hook, hookSpec, r)}");
            }
        }

        string method;
        string options;
        switch (strategy.Kind)
        {
            case StrategyKind.Timeout:
                method = "TimeoutResiliencePipelineBuilderExtensions.AddTimeout";
                options = $"{Res}Timeout.TimeoutStrategyOptions";
                break;
            case StrategyKind.Retry:
                method = "RetryResiliencePipelineBuilderExtensions.AddRetry";
                options = $"{Res}Retry.RetryStrategyOptions<{r}>";
                props.Add("Randomizer = Random");
                if (HasBudget(strategy))
                {
                    props.Add($"Budget = B{I(strategy)}");
                }

                break;
            case StrategyKind.CircuitBreaker:
                method = "CircuitBreakerResiliencePipelineBuilderExtensions.AddCircuitBreaker";
                options = typed ? $"{Res}CircuitBreaker.CircuitBreakerStrategyOptions<{r}>" : $"{Res}CircuitBreaker.CircuitBreakerStrategyOptions";
                foreach (var member in strategy.Members)
                {
                    props.Add($"{member.Slot} = {MemberExpression(member)}");
                }

                break;
            case StrategyKind.Fallback:
                method = "FallbackResiliencePipelineBuilderExtensions.AddFallback";
                options = $"{Res}Fallback.FallbackStrategyOptions<{r}>";
                break;
            case StrategyKind.RateLimiter:
                method = "RateLimiterResiliencePipelineBuilderExtensions.AddRateLimiter";
                options = $"{Res}RateLimiting.RateLimiterStrategyOptions";
                if (Member(strategy, "RateLimiter") is { } limiter)
                {
                    if (Hook(strategy, "OnRejected") is null && strategy.Name is null)
                    {
                        L($"{Ext}{method}({builder}, {MemberExpression(limiter)});");
                        return;
                    }

                    props.Add($"RateLimiter = {(limiter.IsStatic ? "static " : string.Empty)}args => {MemberExpression(limiter)}.AcquireAsync(1, args.CancellationToken)");
                }
                else if (Hook(strategy, "RateLimiter") is { } acquire)
                {
                    props.Add($"RateLimiter = {Lambda(acquire, spec.Hooks.First(h => h.Slot == "RateLimiter"), r)}");
                }

                break;
            case StrategyKind.ConcurrencyLimiter:
                method = "RateLimiterResiliencePipelineBuilderExtensions.AddRateLimiter";
                options = $"{Res}RateLimiting.RateLimiterStrategyOptions";
                props.RemoveAll(p => p.StartsWith("PermitLimit", StringComparison.Ordinal) || p.StartsWith("QueueLimit", StringComparison.Ordinal));
                props.Add($"DefaultRateLimiterOptions = new global::System.Threading.RateLimiting.ConcurrencyLimiterOptions {{ PermitLimit = {BuildValue(strategy, spec.Values[0])}, QueueLimit = {BuildValue(strategy, spec.Values[1])} }}");
                break;
            case StrategyKind.FixedWindowLimiter:
                method = "NativeLimiterResiliencePipelineBuilderExtensions.AddFixedWindowLimiter";
                options = $"{Res}RateLimiting.FixedWindowLimiterStrategyOptions";
                break;
            case StrategyKind.SlidingWindowLimiter:
                method = "NativeLimiterResiliencePipelineBuilderExtensions.AddSlidingWindowLimiter";
                options = $"{Res}RateLimiting.SlidingWindowLimiterStrategyOptions";
                break;
            case StrategyKind.NativeConcurrencyLimiter:
                method = "NativeLimiterResiliencePipelineBuilderExtensions.AddNativeConcurrencyLimiter";
                options = $"{Res}RateLimiting.NativeConcurrencyLimiterStrategyOptions";
                break;
            case StrategyKind.AdaptiveConcurrencyLimiter:
                method = "AdaptiveConcurrencyLimiterResiliencePipelineBuilderExtensions.AddAdaptiveConcurrencyLimiter";
                options = typed ? $"{Res}RateLimiting.AdaptiveConcurrencyLimiterStrategyOptions<{r}>" : $"{Res}RateLimiting.AdaptiveConcurrencyLimiterStrategyOptions";
                break;
            case StrategyKind.Hedging:
                method = "HedgingResiliencePipelineBuilderExtensions.AddHedging";
                options = $"{Res}Hedging.HedgingStrategyOptions<{r}>";
                break;
            case StrategyKind.ChaosFault:
                method = "Simmy.ChaosFaultPipelineBuilderExtensions.AddChaosFault";
                options = $"{Res}Simmy.Fault.ChaosFaultStrategyOptions";
                props.Add("Randomizer = Random");
                if (Hook(strategy, "FaultGenerator") is null && strategy.Settings.FirstOrDefault(s => s.Key == "FaultType") is { } faultType)
                {
                    props.Add($"FaultGenerator = static _ => new {faultType.Literal}()");
                }

                break;
            case StrategyKind.ChaosOutcome:
                method = "Simmy.ChaosOutcomePipelineBuilderExtensions.AddChaosOutcome";
                options = $"{Res}Simmy.Outcomes.ChaosOutcomeStrategyOptions<{r}>";
                props.Add("Randomizer = Random");
                break;
            case StrategyKind.ChaosLatency:
                method = "Simmy.ChaosLatencyPipelineBuilderExtensions.AddChaosLatency";
                options = $"{Res}Simmy.Latency.ChaosLatencyStrategyOptions";
                props.Add("Randomizer = Random");
                break;
            case StrategyKind.ChaosBehavior:
                method = "Simmy.ChaosBehaviorPipelineBuilderExtensions.AddChaosBehavior";
                options = $"{Res}Simmy.Behavior.ChaosBehaviorStrategyOptions";
                props.Add("Randomizer = Random");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(strategy), strategy.Kind, null);
        }

        L($"{Ext}{method}({builder}, new {options}");
        L("{");
        _w.Indent++;
        foreach (var prop in props)
        {
            L(prop + ",");
        }

        _w.Indent--;
        L("});");
    }

    /// <summary>A hook as a delegate for a runtime option (static lambda for a static hook, capturing the owner otherwise).</summary>
    private string Lambda(HookModel hook, HookSpec spec, string r)
    {
        var target = hook.IsStatic ? Self : "owner";
        var prefix = hook.IsStatic ? "static " : string.Empty;
        var generic = hook.IsGeneric ? $"<{r}>" : string.Empty;
        var argument = hook.Parameter switch
        {
            HookParameter.Arguments => "args",
            HookParameter.Outcome => "args.Outcome",
            _ => string.Empty,
        };
        var call = $"{target}.{hook.Method}{generic}({argument})";
        switch (spec.Return)
        {
            case SlotReturn.Event or SlotReturn.Behavior when hook.Return == HookReturn.Void:
                return $"{prefix}args => {{ {call}; return default; }}";
            case SlotReturn.FallbackAction:
                return hook.Return switch
                {
                    HookReturn.Outcome => $"{prefix}args => new {VT}<{Res}Outcome<{r}>>({call})",
                    HookReturn.ValueTaskResult => $"{prefix}async args => {Res}Outcome.FromResult<{r}>(await {call}.ConfigureAwait(false))",
                    HookReturn.Result => $"{prefix}args => {Res}Outcome.FromResultAsValueTask<{r}>({call})",
                    _ => $"{prefix}args => {call}",
                };
            default:
                return $"{prefix}args => {call}";
        }
    }
}
