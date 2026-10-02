using MintPlayer.Resilience.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

internal sealed partial class PipelineEmitter
{
    private string FromException(string exception) => $"{Res}Outcome.FromException<{R}>({exception})";

    private void EmitFlatMethod()
    {
        L("// The flat pipeline (plan S1): one async method, strategies inlined in attribute order (outermost first), values");
        L(_m.Reloadable
            ? "// read from the snapshot taken at entry (S6). Telemetry is compiled out: M6 passes an enabled TTelemetry."
            : "// folded in. Telemetry is compiled out: M6 passes an enabled TTelemetry.");
        if (_m.PooledAsync)
        {
            L("[global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]");
        }

        var typeParameters = IsGeneric ? "TResult, TCallback, TShape, TOut, TTelemetry" : "TCallback, TShape, TOut, TTelemetry";
        L($"private {Static}async {VT}<TOut> __RunAsync<{typeParameters}>(__Runtime rt, {P}ExecutionFrame<{R}> frame, TCallback callback, TTelemetry telemetry)");
        _w.Indent++;
        L($"where TCallback : struct, {P}ICallback<{R}>");
        L($"where TShape : struct, {P}IOutcomeShape<{R}, TOut>");
        L($"where TTelemetry : struct, {P}IPipelineTelemetry");
        _w.Indent--;
        using (Open(string.Empty))
        {
            L("var capture = frame.ContinueOnCapturedContext;");
            L("var started = telemetry.OnPipelineExecuting(frame);");
            EmitLevel(0);
            L("telemetry.OnPipelineExecuted(frame, started);");
            L("var outcome = frame.Outcome;");
            L("frame.Return();");
            L("return TShape.Complete(outcome);");
        }
    }

    private void EmitLevel(int index)
    {
        if (index == Count)
        {
            EmitCallback();
            return;
        }

        var strategy = _m.Strategies[index];
        L($"// [{I(strategy)}] {StrategySchema.Of(strategy.Kind).DefaultKey}{(strategy.Name is null ? string.Empty : " \"" + strategy.Name + "\"")}");
        switch (strategy.Kind)
        {
            case StrategyKind.Timeout:
                EmitTimeout(strategy, index);
                break;
            case StrategyKind.Retry:
                EmitRetry(strategy, index);
                break;
            case StrategyKind.Fallback:
                EmitFallback(strategy, index);
                break;
            default:
                EmitDelegated(strategy, index);
                break;
        }
    }

    private void EmitCallback()
    {
        using (Open("try"))
        {
            if (IsGeneric)
            {
                using (Open("if (TCallback.IsVoid)"))
                {
                    L("await callback.InvokeVoidAsync(frame).ConfigureAwait(capture);");
                    L($"frame.Outcome = {Res}Outcome.FromResult<{R}>(({R})(object){P}VoidResult.Instance);");
                }

                using (Open("else"))
                {
                    L($"frame.Outcome = {Res}Outcome.FromResult<{R}>(await callback.InvokeAsync(frame).ConfigureAwait(capture));");
                }
            }
            else
            {
                L($"frame.Outcome = {Res}Outcome.FromResult<{R}>(await callback.InvokeAsync(frame).ConfigureAwait(capture));");
            }
        }

        using (Open($"catch ({Ex} callbackException)"))
        {
            L($"frame.Outcome = {FromException("callbackException")};");
        }
    }

    /// <summary>An inline call of a hook from the flat method.</summary>
    private string Call(HookModel hook, string arguments, string outcome)
    {
        var target = hook.IsStatic ? Self : "this";
        var generic = hook.IsGeneric ? $"<{R}>" : string.Empty;
        var argument = hook.Parameter switch
        {
            HookParameter.Arguments => arguments,
            HookParameter.Outcome => outcome,
            _ => string.Empty,
        };
        return $"{target}.{hook.Method}{generic}({argument})";
    }

    /// <summary>Raises an event hook: awaited when it returns a <c>ValueTask</c>, called when it is void.</summary>
    private void RaiseEvent(HookModel hook, string arguments)
    {
        var call = Call(hook, arguments, arguments);
        L(hook.Return == HookReturn.Void ? $"{call};" : $"await {call}.ConfigureAwait(capture);");
    }

    // Port of TimeoutStrategy: EnterAsync, the inner part, ExitAsync.
    private void EmitTimeout(StrategyModel strategy, int index)
    {
        var i = I(strategy);
        using (Open(string.Empty))
        {
            var generator = Hook(strategy, "TimeoutGenerator");
            L($"{TS} timeout{i};");
            L($"var entered{i} = true;");
            if (generator is not null)
            {
                using (Open("try"))
                {
                    L($"timeout{i} = {Call(generator, $"{Support}.TimeoutGenerator(frame)", string.Empty)};");
                }

                using (Open($"catch ({Ex} generatorException{i})"))
                {
                    L($"frame.Outcome = {FromException($"generatorException{i}")};");
                    L($"timeout{i} = default;");
                    L($"entered{i} = false;");
                }
            }
            else
            {
                L($"timeout{i} = {FlatValue(strategy, "Timeout")};");
            }

            using (Open($"if (entered{i})"))
            {
                L($"global::System.Threading.CancellationTokenSource? source{i} = null;");
                L($"{CT} previous{i} = default;");
                L($"global::System.Threading.CancellationTokenRegistration registration{i} = default;");
                using (Open($"if (timeout{i} > {TS}.Zero)"))
                {
                    L("// Zero, negative and InfiniteTimeSpan: no timeout for this execution (Polly's ShouldApplyTimeout).");
                    L($"previous{i} = frame.CancellationToken;");
                    L($"source{i} = rt.Pool.Rent(timeout{i});");
                    L($"registration{i} = {Support}.Link(previous{i}, source{i});");
                    L($"frame.SetCancellationToken(source{i}.Token);");
                }

                EmitLevel(index + 1);

                using (Open($"if (source{i} is not null)"))
                {
                    L($"var fired{i} = source{i}.IsCancellationRequested;");
                    L($"frame.SetCancellationToken(previous{i});");
                    L($"registration{i}.Dispose();");
                    L($"rt.Pool.Return(source{i});");
                    using (Open($"if (fired{i} && {Support}.RawException(frame.Outcome) is global::System.OperationCanceledException cancellation{i} && !previous{i}.IsCancellationRequested)"))
                    {
                        L($"frame.Outcome = {Support}.TimeoutRejection<{R}>(timeout{i}, cancellation{i});");
                        if (Hook(strategy, "OnTimeout") is { } onTimeout)
                        {
                            using (Open("try"))
                            {
                                RaiseEvent(onTimeout, $"{Support}.OnTimeout(frame, timeout{i})");
                            }

                            using (Open($"catch ({Ex} eventException{i})"))
                            {
                                L($"frame.Outcome = {FromException($"eventException{i}")};");
                            }
                        }
                    }
                }
            }
        }
    }

    // Port of RetryStrategy: the deposit on entry, then the inner part in a loop whose exit decides whether to repeat.
    private void EmitRetry(StrategyModel strategy, int index)
    {
        var i = I(strategy);
        var hasBudget = Member(strategy, "Budget") is not null || _m.Budget is not null;
        using (Open(string.Empty))
        {
            if (hasBudget)
            {
                L($"var budget{i} = rt.B{i};");
                L($"budget{i}?.Deposit();");
            }

            L($"var attempt{i} = 0;");
            L($"var jitter{i} = 0d;");
            L($"var start{i} = rt.TimeProvider.GetTimestamp();");
            using (Open("while (true)"))
            {
                EmitLevel(index + 1);

                L($"var again{i} = false;");
                using (Open("try"))
                {
                    L($"var outcome{i} = frame.Outcome;");
                    var predicate = Hook(strategy, "ShouldHandle") is { } shouldHandle
                        ? Call(shouldHandle, $"{Support}.RetryPredicate(frame, outcome{i}, attempt{i})", $"outcome{i}")
                        : $"{Support}.HandleByDefault(outcome{i})";
                    L("// As Polly: the predicate runs for every attempt, the last one included; int.MaxValue retries forever.");
                    var max = FlatValue(strategy, "MaxRetryAttempts");
                    using (Open($"if ({predicate} && !(attempt{i} != int.MaxValue && attempt{i} >= {max}))"))
                    {
                        if (hasBudget)
                        {
                            using (Open($"if (budget{i} is not null && !budget{i}.TryWithdraw())"))
                            {
                                L("// Out of budget: this attempt becomes the last one, and its outcome is returned.");
                                if (Hook(strategy, "OnBudgetExhausted") is { } onExhausted)
                                {
                                    RaiseEvent(onExhausted, $"{Support}.OnRetryBudgetExhausted(frame, outcome{i}, attempt{i})");
                                }
                            }

                            using (Open("else"))
                            {
                                EmitRetryDelay(strategy, i);
                            }
                        }
                        else
                        {
                            EmitRetryDelay(strategy, i);
                        }
                    }
                }

                using (Open($"catch ({Ex} exitException{i})"))
                {
                    L($"frame.Outcome = {FromException($"exitException{i}")};");
                    L($"again{i} = false;");
                }

                using (Open($"if (!again{i})"))
                {
                    L("break;");
                }
            }
        }
    }

    private void EmitRetryDelay(StrategyModel strategy, string i)
    {
        var backoff = FlatValue(strategy, "BackoffType");
        var jitter = FlatValue(strategy, "UseJitter");
        var delay = FlatValue(strategy, "Delay");
        var maxDelay = FlatValue(strategy, "MaxDelay");
        L($"var delay{i} = {Support}.GetRetryDelay({backoff}, {jitter}, attempt{i}, {delay}, {maxDelay}, ref jitter{i}, rt.Random);");
        if (Hook(strategy, "DelayGenerator") is { } generator)
        {
            var call = Call(generator, $"{Support}.RetryDelayGenerator(frame, outcome{i}, attempt{i})", $"outcome{i}");
            using (Open($"if (({TS}?){call} is {TS} generated{i} && {Support}.IsValidDelay(generated{i}))"))
            {
                L($"delay{i} = generated{i};");
            }
        }

        L($"var duration{i} = rt.TimeProvider.GetElapsedTime(start{i});");
        if (Hook(strategy, "OnRetry") is { } onRetry)
        {
            RaiseEvent(onRetry, $"{Support}.OnRetry(frame, outcome{i}, attempt{i}, delay{i}, duration{i})");
        }

        L("// The discarded result (e.g. an HttpResponseMessage) is disposed, as in Polly; value types are skipped.");
        L($"await {Support}.DisposeDiscardedAsync(outcome{i}, frame.IsSynchronous).ConfigureAwait(capture);");
        using (Open("try"))
        {
            L("frame.CancellationToken.ThrowIfCancellationRequested();");
            using (Open($"if (delay{i} > {TS}.Zero)"))
            {
                L($"await {Support}.DelayAsync(rt.TimeProvider, delay{i}, frame).ConfigureAwait(capture);");
            }

            L($"again{i} = true;");
        }

        using (Open($"catch (global::System.OperationCanceledException cancellation{i})"))
        {
            L($"frame.Outcome = {FromException($"cancellation{i}")};");
        }

        using (Open($"if (again{i})"))
        {
            using (Open($"if (attempt{i} != int.MaxValue)"))
            {
                L($"attempt{i}++;");
            }

            L($"start{i} = rt.TimeProvider.GetTimestamp();");
        }
    }

    // Port of FallbackStrategy: an exit that replaces a handled outcome.
    private void EmitFallback(StrategyModel strategy, int index)
    {
        var i = I(strategy);
        EmitLevel(index + 1);
        using (Open("try"))
        {
            L($"var outcome{i} = frame.Outcome;");
            var predicate = Hook(strategy, "ShouldHandle") is { } shouldHandle
                ? Call(shouldHandle, $"{Support}.FallbackPredicate(frame, outcome{i})", $"outcome{i}")
                : $"{Support}.HandleByDefault(outcome{i})";
            using (Open($"if ({predicate})"))
            {
                if (Hook(strategy, "OnFallback") is { } onFallback)
                {
                    RaiseEvent(onFallback, $"{Support}.OnFallback(frame, outcome{i})");
                }

                var action = Hook(strategy, "FallbackAction")!;
                var call = Call(action, $"{Support}.FallbackAction(frame, outcome{i})", $"outcome{i}");
                using (Open("try"))
                {
                    L(action.Return switch
                    {
                        HookReturn.Outcome => $"frame.Outcome = {call};",
                        HookReturn.ValueTaskResult => $"frame.Outcome = {Res}Outcome.FromResult<{R}>(await {call}.ConfigureAwait(capture));",
                        HookReturn.Result => $"frame.Outcome = {Res}Outcome.FromResult<{R}>({call});",
                        _ => $"frame.Outcome = await {call}.ConfigureAwait(capture);",
                    });
                }

                using (Open($"catch ({Ex} actionException{i})"))
                {
                    L($"frame.Outcome = {FromException($"actionException{i}")};");
                }
            }
        }

        using (Open($"catch ({Ex} exitException{i})"))
        {
            L($"frame.Outcome = {FromException($"exitException{i}")};");
        }
    }

    // A breaker, limiter or chaos strategy: the runtime's own hooks, on this frame and slot.
    private void EmitDelegated(StrategyModel strategy, int index)
    {
        var i = I(strategy);
        using (Open(string.Empty))
        {
            L(IsGeneric ? $"var strategy{i} = rt.S{i}.For<{R}>();" : $"var strategy{i} = rt.S{i};");
            L($"bool entered{i};");
            using (Open("try"))
            {
                L($"entered{i} = await strategy{i}.EnterAsync(frame, {i}).ConfigureAwait(capture);");
            }

            using (Open($"catch ({Ex} enterException{i})"))
            {
                L($"frame.Outcome = {FromException($"enterException{i}")};");
                L($"entered{i} = false;");
            }

            using (Open($"if (entered{i})"))
            {
                EmitLevel(index + 1);
                using (Open("try"))
                {
                    L($"_ = await strategy{i}.ExitAsync(frame, {i}).ConfigureAwait(capture);");
                }

                using (Open($"catch ({Ex} exitException{i})"))
                {
                    L($"frame.Outcome = {FromException($"exitException{i}")};");
                }
            }
        }
    }
}
