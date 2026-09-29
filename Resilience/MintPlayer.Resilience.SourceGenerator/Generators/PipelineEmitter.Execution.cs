using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

internal sealed partial class PipelineEmitter
{
    private enum Mode
    {
        Async,
        Sync,
        AsyncVoid,
        SyncVoid,
    }

    /// <summary>One public execution member, and how it maps onto the flat method (or the runtime pipeline).</summary>
    private sealed class Overload
    {
        public string Name = string.Empty;
        public string TypeParameters = string.Empty;
        public string Parameters = string.Empty;
        public string ReturnType = string.Empty;

        /// <summary>The result type of the frame and the callback (the pipeline's, or <c>VoidResult</c>).</summary>
        public string Core = string.Empty;
        public string Shape = string.Empty;
        public string Out = string.Empty;
        public string CallbackType = string.Empty;
        public string CallbackArguments = string.Empty;
        public string Token = "default";
        public string Context = "null";
        public Mode Mode;
        public string Arguments = string.Empty;
        public bool HasState;
        public bool HasContext;
        public bool HasToken;
    }

    private void EmitExecutionMembers()
    {
        var first = true;
        foreach (var overload in Overloads())
        {
            if (!first)
            {
                Blank();
            }

            first = false;
            EmitOverload(overload);
        }
    }

    private void EmitOverload(Overload o)
    {
        var what = o.Mode switch
        {
            Mode.Sync or Mode.SyncVoid => "Executes a synchronous <paramref name=\"callback\"/> through the pipeline. Delays block the calling thread.",
            _ when o.Name == "TryExecuteAsync" => "Executes <paramref name=\"callback\"/> through the pipeline and returns the outcome instead of throwing.",
            _ => "Executes <paramref name=\"callback\"/> through the pipeline.",
        };
        L($"/// <summary>{what}</summary>");
        if (o.TypeParameters.Contains("TResult"))
        {
            L("/// <typeparam name=\"TResult\">The type of the result.</typeparam>");
        }

        if (o.HasState)
        {
            L("/// <typeparam name=\"TState\">The type of the state.</typeparam>");
        }

        L(o.HasState
            ? "/// <param name=\"callback\">The callback; a <c>static</c> lambda keeps the call allocation-free.</param>"
            : "/// <param name=\"callback\">The callback.</param>");
        if (o.HasContext)
        {
            L("/// <param name=\"context\">The context; its <see cref=\"global::MintPlayer.Resilience.ResilienceContext.CancellationToken\"/> is the caller's token.</param>");
        }

        if (o.HasState)
        {
            L("/// <param name=\"state\">The state passed to the callback.</param>");
        }

        if (o.HasToken)
        {
            L("/// <param name=\"cancellationToken\">The caller's cancellation token.</param>");
        }

        switch (o.Mode)
        {
            case Mode.SyncVoid:
                break;
            case Mode.AsyncVoid:
                L("/// <returns>A pooled task that completes with the execution (await it once); a failure or rejection is thrown.</returns>");
                break;
            case Mode.Sync:
                L("/// <returns>The result; a failure or rejection is thrown.</returns>");
                break;
            default:
                L(o.Name == "TryExecuteAsync"
                    ? "/// <returns>A pooled task (await it once) with the outcome: a result, the callback's exception, or a rejection, which allocates nothing.</returns>"
                    : "/// <returns>A pooled task (await it once) with the result; a failure or rejection is thrown.</returns>");
                break;
        }

        using (Open($"public {Static}{o.ReturnType} {o.Name}{o.TypeParameters}({o.Parameters})"))
        {
            L("global::System.ArgumentNullException.ThrowIfNull(callback);");
            if (o.HasContext)
            {
                L("global::System.ArgumentNullException.ThrowIfNull(context);");
            }

            if (Interpreted)
            {
                var forward = $"__Current.Pipeline.{o.Name}({o.Arguments})";
                L(o.Mode == Mode.SyncVoid ? $"{forward};" : $"return {forward};");
                return;
            }

            var typeArguments = IsGeneric
                ? $"{o.Core}, {o.CallbackType}, {o.Shape}, {o.Out}, {P}NoTelemetry"
                : $"{o.CallbackType}, {o.Shape}, {o.Out}, {P}NoTelemetry";
            var sync = o.Mode is Mode.Sync or Mode.SyncVoid ? "true" : "false";
            var call = $"__RunAsync<{typeArguments}>(__Current, {P}ExecutionFrame<{o.Core}>.Rent(__StrategyCount, {o.Token}, {o.Context}, {sync}), new {o.CallbackType}({o.CallbackArguments}), default)";
            switch (o.Mode)
            {
                case Mode.Async:
                    L($"return {call};");
                    break;
                case Mode.Sync:
                    L($"return {Support}.Wait({call});");
                    break;
                case Mode.AsyncVoid:
                    L($"return {Support}.ToVoidTask({call}, {(_m.PooledAsync ? "true" : "false")});");
                    break;
                case Mode.SyncVoid:
                    L($"_ = {Support}.Wait({call});");
                    break;
            }
        }
    }

    private List<Overload> Overloads()
    {
        var list = new List<Overload>();
        var r = R;
        string Tp(bool state) => IsGeneric
            ? state ? "<TResult, TState>" : "<TResult>"
            : state ? "<TState>" : string.Empty;

        foreach (var (name, shape, output) in new[]
        {
            ("ExecuteAsync", $"{P}ResultShape<{r}>", r),
            ("TryExecuteAsync", $"{P}OutcomeShape<{r}>", $"{Res}Outcome<{r}>"),
        })
        {
            var ret = $"{VT}<{output}>";
            list.Add(new Overload
            {
                Name = name, TypeParameters = Tp(false), ReturnType = ret, Core = r, Shape = shape, Out = output, Mode = Mode.Async,
                Parameters = $"{Func}<{CT}, {VT}<{r}>> callback, {CT} cancellationToken = default",
                CallbackType = $"{P}AsyncCallback<{Func}<{CT}, {VT}<{r}>>, {r}>", CallbackArguments = "static (f, ct) => f(ct), callback",
                Token = "cancellationToken", Arguments = "callback, cancellationToken", HasToken = true,
            });
            list.Add(new Overload
            {
                Name = name, TypeParameters = Tp(true), ReturnType = ret, Core = r, Shape = shape, Out = output, Mode = Mode.Async,
                Parameters = $"{Func}<TState, {CT}, {VT}<{r}>> callback, TState state, {CT} cancellationToken = default",
                CallbackType = $"{P}AsyncCallback<TState, {r}>", CallbackArguments = "callback, state",
                Token = "cancellationToken", Arguments = "callback, state, cancellationToken", HasToken = true, HasState = true,
            });
            list.Add(new Overload
            {
                Name = name, TypeParameters = Tp(false), ReturnType = ret, Core = r, Shape = shape, Out = output, Mode = Mode.Async,
                Parameters = $"{Func}<{RC}, {VT}<{r}>> callback, {RC} context",
                CallbackType = $"{P}AsyncContextCallback<{Func}<{RC}, {VT}<{r}>>, {r}>", CallbackArguments = "static (c, f) => f(c), callback",
                Context = "context", Arguments = "callback, context", HasContext = true,
            });
            list.Add(new Overload
            {
                Name = name, TypeParameters = Tp(true), ReturnType = ret, Core = r, Shape = shape, Out = output, Mode = Mode.Async,
                Parameters = $"{Func}<{RC}, TState, {VT}<{r}>> callback, {RC} context, TState state",
                CallbackType = $"{P}AsyncContextCallback<TState, {r}>", CallbackArguments = "callback, state",
                Context = "context", Arguments = "callback, context, state", HasContext = true, HasState = true,
            });
        }

        var resultShape = $"{P}ResultShape<{r}>";
        void Sync(string parameters, string callbackType, string callbackArguments, string arguments, bool state, bool token, bool context)
            => list.Add(new Overload
            {
                Name = "Execute", TypeParameters = Tp(state), ReturnType = r, Core = r, Shape = resultShape, Out = r, Mode = Mode.Sync,
                Parameters = parameters, CallbackType = callbackType, CallbackArguments = callbackArguments, Arguments = arguments,
                Token = token ? "cancellationToken" : "default", Context = context ? "context" : "null",
                HasState = state, HasToken = token, HasContext = context,
            });

        Sync($"{Func}<{r}> callback", $"{P}SyncCallback<{Func}<{r}>, {r}>", "static (f, _) => f(), callback", "callback", false, false, false);
        Sync($"{Func}<{CT}, {r}> callback, {CT} cancellationToken = default", $"{P}SyncCallback<{Func}<{CT}, {r}>, {r}>", "static (f, ct) => f(ct), callback", "callback, cancellationToken", false, true, false);
        Sync($"{Func}<TState, {r}> callback, TState state", $"{P}SyncCallback<({Func}<TState, {r}> Callback, TState State), {r}>", "static (s, _) => s.Callback(s.State), (callback, state)", "callback, state", true, false, false);
        Sync($"{Func}<TState, {CT}, {r}> callback, TState state, {CT} cancellationToken = default", $"{P}SyncCallback<TState, {r}>", "callback, state", "callback, state, cancellationToken", true, true, false);
        Sync($"{Func}<{RC}, {r}> callback, {RC} context", $"{P}SyncContextCallback<{Func}<{RC}, {r}>, {r}>", "static (c, f) => f(c), callback", "callback, context", false, false, true);
        Sync($"{Func}<{RC}, TState, {r}> callback, {RC} context, TState state", $"{P}SyncContextCallback<TState, {r}>", "callback, state", "callback, context, state", true, false, true);

        if (!IsGeneric)
        {
            return list;
        }

        // The void overloads of a generic pipeline, as on a non-generic ResiliencePipeline.
        var v = $"{P}VoidResult";
        var voidShape = $"{P}ResultShape<{v}>";
        void Void(Mode mode, string parameters, string callbackType, string callbackArguments, bool state, bool token, bool context)
            => list.Add(new Overload
            {
                Name = mode == Mode.AsyncVoid ? "ExecuteAsync" : "Execute",
                TypeParameters = state ? "<TState>" : string.Empty,
                ReturnType = mode == Mode.AsyncVoid ? VT : "void",
                Core = v, Shape = voidShape, Out = v, Mode = mode,
                Parameters = parameters, CallbackType = callbackType, CallbackArguments = callbackArguments,
                Token = token ? "cancellationToken" : "default", Context = context ? "context" : "null",
                HasState = state, HasToken = token, HasContext = context,
            });

        Void(Mode.AsyncVoid, $"{Func}<{CT}, {VT}> callback, {CT} cancellationToken = default", $"{P}AsyncVoidCallback<{Func}<{CT}, {VT}>>", "static (f, ct) => f(ct), callback", false, true, false);
        Void(Mode.AsyncVoid, $"{Func}<TState, {CT}, {VT}> callback, TState state, {CT} cancellationToken = default", $"{P}AsyncVoidCallback<TState>", "callback, state", true, true, false);
        Void(Mode.AsyncVoid, $"{Func}<{RC}, {VT}> callback, {RC} context", $"{P}AsyncVoidContextCallback<{Func}<{RC}, {VT}>>", "static (c, f) => f(c), callback", false, false, true);
        Void(Mode.AsyncVoid, $"{Func}<{RC}, TState, {VT}> callback, {RC} context, TState state", $"{P}AsyncVoidContextCallback<TState>", "callback, state", true, false, true);
        Void(Mode.SyncVoid, $"{Act} callback", $"{P}SyncVoidCallback<{Act}>", "static (f, _) => f(), callback", false, false, false);
        Void(Mode.SyncVoid, $"{Act}<{CT}> callback, {CT} cancellationToken = default", $"{P}SyncVoidCallback<{Act}<{CT}>>", "static (f, ct) => f(ct), callback", false, true, false);
        Void(Mode.SyncVoid, $"{Act}<TState> callback, TState state", $"{P}SyncVoidCallback<({Act}<TState> Callback, TState State)>", "static (s, _) => s.Callback(s.State), (callback, state)", true, false, false);
        Void(Mode.SyncVoid, $"{Act}<TState, {CT}> callback, TState state, {CT} cancellationToken = default", $"{P}SyncVoidCallback<TState>", "callback, state", true, true, false);
        Void(Mode.SyncVoid, $"{Act}<{RC}> callback, {RC} context", $"{P}SyncVoidContextCallback<{Act}<{RC}>>", "static (c, f) => f(c), callback", false, false, true);
        Void(Mode.SyncVoid, $"{Act}<{RC}, TState> callback, {RC} context, TState state", $"{P}SyncVoidContextCallback<TState>", "callback, state", true, false, true);
        return list;
    }
}
