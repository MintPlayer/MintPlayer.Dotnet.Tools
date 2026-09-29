namespace MintPlayer.Resilience.SourceGenerator.Tests.Generators;

/// <summary>
/// What the generator emits for each shape, on small fixtures: the form (static or DI), the result type (typed,
/// inferred or generic), flattening versus forwarding, reload, hook binding and the classes it must leave alone.
/// Every fixture that should generate is also compiled.
/// </summary>
public class GeneratorOutputTests
{
    private static GeneratorResult Run(string body) => Harness.Instance.RunGenerator(Harness.Generator, Harness.Usings + "namespace Demo;\n\n" + body);

    private static string Generate(string body)
    {
        var run = Run(body);
        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty("the fixture must generate a pipeline");
        return run.AllSources;
    }

    [Fact]
    public void StaticForm_EmitsStaticMembersAndAFlatMethod()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Retry(MaxRetryAttempts = 2, DelayMs = 0)]
            [Timeout(TimeoutMs = 1_000)]
            public sealed partial class P;
            """);

        source.Should().Contain("public static global::System.Threading.Tasks.ValueTask<int> ExecuteAsync(");
        source.Should().Contain("public static global::System.Threading.Tasks.ValueTask<global::MintPlayer.Resilience.Outcome<int>> TryExecuteAsync<TState>(");
        source.Should().Contain("public static int Execute<TState>(");
        source.Should().Contain("private static async global::System.Threading.Tasks.ValueTask<TOut> __RunAsync<");
        source.Should().Contain("IsInstancePipeline => false");
        source.Should().NotContain("ResiliencePipeline<int> Pipeline", "a pipeline without hedging is flattened, not forwarded");
    }

    [Fact]
    public void Constants_AreFoldedIntoTheFlatMethod()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Retry(MaxRetryAttempts = 7, DelayMs = 250, BackoffType = DelayBackoffType.Linear)]
            public sealed partial class P;
            """);

        source.Should().Contain("attempt0 >= 7");
        source.Should().Contain("GetRetryDelay((global::MintPlayer.Resilience.DelayBackoffType)1, false, attempt0, global::System.TimeSpan.FromMilliseconds(250), (global::System.TimeSpan?)null");
    }

    [Fact]
    public void PooledAsync_IsTheDefault_AndCanBeTurnedOff()
    {
        const string attribute = "PoolingAsyncValueTaskMethodBuilder<>";
        Generate("[ResiliencePipeline<int>] [Retry] public sealed partial class P;").Should().Contain(attribute);
        Generate("[ResiliencePipeline<int>(PooledAsync = false)] [Retry] public sealed partial class P;").Should().NotContain(attribute);
    }

    [Fact]
    public void AnInstanceHook_MakesTheDIForm()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Retry(DelayMs = 0)]
            public sealed partial class P
            {
                [OnRetry] ValueTask Log(OnRetryArguments<int> args) => default;
            }
            """);

        source.Should().Contain("public global::System.Threading.Tasks.ValueTask<int> ExecuteAsync(");
        source.Should().Contain("private async global::System.Threading.Tasks.ValueTask<TOut> __RunAsync<");
        source.Should().Contain("await this.Log(");
        source.Should().Contain("IsInstancePipeline => true");
    }

    [Fact]
    public void AConstructorWithParameters_MakesTheDIForm_AndCreateResolvesThem()
    {
        var source = Generate("""
            public sealed class Clock;

            [ResiliencePipeline<int>]
            [Retry(DelayMs = 0)]
            public sealed partial class P(Clock clock, int retries = 3)
            {
                public Clock Clock => clock;
            }
            """);

        source.Should().Contain("return new global::Demo.P(__Resolve<global::Demo.Clock>(services, \"clock\"), __ResolveOrDefault<int>(services, (int)3));");
        source.Should().Contain("IsInstancePipeline => true");
    }

    [Fact]
    public void ATypedHook_InfersTheResultType()
    {
        var source = Generate("""
            [ResiliencePipeline]
            [Retry(DelayMs = 0)]
            public sealed partial class P
            {
                [RetryWhen] static bool Handle(Outcome<string> outcome) => outcome.Exception is not null;
            }
            """);

        source.Should().Contain("public static global::System.Threading.Tasks.ValueTask<string> ExecuteAsync(");
        source.Should().NotContain("ExecuteAsync<TResult>", "a typed hook fixes the result type");
    }

    [Fact]
    public void NoTypedHook_MakesAGenericPipeline_WithVoidOverloads()
    {
        var source = Generate("""
            [ResiliencePipeline]
            [Retry(DelayMs = 0)]
            [CircuitBreaker]
            public sealed partial class P
            {
                [BreakWhen] static bool Breaks<TResult>(Outcome<TResult> outcome) => outcome.Exception is not null;
            }
            """);

        source.Should().Contain("public static global::System.Threading.Tasks.ValueTask<TResult> ExecuteAsync<TResult>(");
        source.Should().Contain("public static global::System.Threading.Tasks.ValueTask ExecuteAsync(global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask> callback");
        source.Should().Contain("public static void Execute(global::System.Action callback)");
        source.Should().Contain("rt.S1.For<TResult>()", "a generic pipeline shares one breaker across result types");
        source.Should().Contain("Breaks<object>(args.Outcome)", "the runtime breaker of a generic pipeline sees object results, as in Polly's non-generic pipeline");
    }

    [Fact]
    public void Hedging_ForwardsToARuntimePipeline()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Timeout(TimeoutMs = 5_000)]
            [Hedging(MaxHedgedAttempts = 2, DelayMs = 50)]
            public sealed partial class P;
            """);

        source.Should().Contain("public readonly global::MintPlayer.Resilience.ResiliencePipeline<int> Pipeline;");
        source.Should().Contain("return __Current.Pipeline.ExecuteAsync(callback, state, cancellationToken);");
        source.Should().Contain("HedgingResiliencePipelineBuilderExtensions.AddHedging(builder,");
        source.Should().NotContain("__RunAsync", "a pipeline with hedging is not flattened");
    }

    [Fact]
    public void Reloadable_EmitsTheOptionsClass_AndReadsTheSnapshot()
    {
        var source = Generate("""
            [ResiliencePipeline<int>(Reloadable = true, Name = "Catalog")]
            [Timeout(TimeoutMs = 10_000, Name = "Total")]
            [Retry(MaxRetryAttempts = 3, DelayMs = 0)]
            [CircuitBreaker(FailureRatio = 0.5)]
            [Timeout(TimeoutMs = 2_000, Name = "Attempt")]
            public sealed partial class P;
            """);

        source.Should().Contain("public sealed class POptions");
        source.Should().Contain("public TimeoutSection Total { get; set; } = new() { Timeout = global::System.TimeSpan.FromMilliseconds(10000) };");
        source.Should().Contain("public TimeoutSection Attempt { get; set; } = new() { Timeout = global::System.TimeSpan.FromMilliseconds(2000) };");
        source.Should().Contain("public CircuitBreakerSection CircuitBreaker { get; set; } = new() { FailureRatio = 0.5d,");
        source.Should().Contain("IReloadableResiliencePipeline<global::Demo.P, global::Demo.POptions>");
        source.Should().Contain("public static string DefaultSectionPath => \"Resilience:Catalog\";");
        source.Should().Contain("public static bool TryApply(global::Demo.POptions options, out string? error)");
        source.Should().Contain("attempt1 >= rt.V1_MaxRetryAttempts", "a reloadable value is read from the snapshot, not folded");
        source.Should().Contain("__Same2(previous.Options.CircuitBreaker, options.CircuitBreaker)", "an unchanged breaker section keeps its state");
    }

    [Fact]
    public void TwoUnnamedStrategiesOfOneKind_GetDistinctOptionKeys()
    {
        var source = Generate("""
            [ResiliencePipeline<int>(Reloadable = true)]
            [Timeout(TimeoutMs = 10_000)]
            [Timeout(TimeoutMs = 2_000)]
            public sealed partial class P;
            """);

        source.Should().Contain("public TimeoutSection Timeout { get; set; }");
        source.Should().Contain("public TimeoutSection Timeout2 { get; set; }");
    }

    [Fact]
    public void AHookNamedOnTheStrategy_WinsOverAHookAttribute()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Retry(DelayMs = 0, ShouldHandle = nameof(Named))]
            public sealed partial class P
            {
                internal static bool Named(Outcome<int> outcome) => true;

                [RetryWhen] internal static bool ByAttribute(Outcome<int> outcome) => false;
            }
            """);

        source.Should().Contain("global::Demo.P.Named(outcome0)");
        source.Should().NotContain("ByAttribute");
    }

    [Fact]
    public void AHookAttribute_BindsToTheStrategyItNames()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Timeout(TimeoutMs = 10_000, Name = "Total")]
            [Retry(DelayMs = 0)]
            [Timeout(TimeoutMs = 2_000, Name = "Attempt")]
            public sealed partial class P
            {
                [OnTimeout("Attempt")] internal static void AttemptTimedOut(OnTimeoutArguments args) { }
            }
            """);

        var total = source.IndexOf("// [0] Timeout \"Total\"", StringComparison.Ordinal);
        var attempt = source.IndexOf("// [2] Timeout \"Attempt\"", StringComparison.Ordinal);
        var hook = source.IndexOf("AttemptTimedOut(", StringComparison.Ordinal);
        total.Should().BeGreaterThan(-1);
        attempt.Should().BeGreaterThan(total);
        hook.Should().BeGreaterThan(attempt, "the hook belongs to the inner, attempt timeout");
        source.Split("AttemptTimedOut(").Length.Should().Be(2, "the hook is bound once");
    }

    [Fact]
    public void AHookAttribute_WithoutAMatchingStrategy_IsIgnored()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Retry(DelayMs = 0)]
            public sealed partial class P
            {
                [OnFallback] internal static ValueTask Unused(OnFallbackArguments<int> args) => default;
            }
            """);

        source.Should().NotContain("Unused");
    }

    [Fact]
    public void AnAmbiguousHookAttribute_IsNotBound()
    {
        var source = Generate("""
            [ResiliencePipeline<int>]
            [Timeout(TimeoutMs = 10_000)]
            [Timeout(TimeoutMs = 2_000)]
            public sealed partial class P
            {
                [OnTimeout] internal static void Which(OnTimeoutArguments args) { }
            }
            """);

        source.Should().NotContain("Which(", "two timeouts and no strategy name: the analyzer reports it, the generator binds nothing");
    }

    [Fact]
    public void FallbackActionShapes_AllCompile()
    {
        Generate("""
            [ResiliencePipeline<int>] [Fallback] public sealed partial class A { [FallbackWith] static int F() => 0; }
            [ResiliencePipeline<int>] [Fallback] public sealed partial class B { [FallbackWith] static Outcome<int> F(Outcome<int> o) => Outcome.FromResult(1); }
            [ResiliencePipeline<int>] [Fallback] public sealed partial class C { [FallbackWith] static ValueTask<int> F(FallbackActionArguments<int> a) => new(2); }
            [ResiliencePipeline<int>] [Fallback] public sealed partial class D { [FallbackWith] static ValueTask<Outcome<int>> F(FallbackActionArguments<int> a) => Outcome.FromResultAsValueTask(3); }
            [ResiliencePipeline<int>] [Hedging] [Fallback] public sealed partial class E { [FallbackWith] static ValueTask<int> F() => new(4); }
            """);
    }

    [Fact]
    public void ANestedPipeline_RedeclaresItsContainingTypes()
    {
        var source = Generate("""
            public static partial class Outer
            {
                public partial struct Middle
                {
                    [ResiliencePipeline<int>] [Retry(DelayMs = 0)] public sealed partial class P;
                }
            }
            """);

        source.Should().Contain("partial class Outer");
        source.Should().Contain("partial struct Middle");
        source.Should().Contain("IGeneratedResiliencePipeline<global::Demo.Outer.Middle.P>");
    }

    [Theory]
    [InlineData("[ResiliencePipeline<int>] [Retry] public sealed class NotPartial;")]
    [InlineData("[ResiliencePipeline<int>] [Retry] public static partial class IsStatic;")]
    [InlineData("[ResiliencePipeline<int>] [Retry] public sealed partial class Generic<T>;")]
    [InlineData("public class NotPartialOuter { [ResiliencePipeline<int>] [Retry] public sealed partial class P; }")]
    [InlineData("[ResiliencePipeline<int>] [Fallback] public sealed partial class NoFallbackAction;")]
    [InlineData("[ResiliencePipeline] [Hedging] public sealed partial class HedgingNeedsATypedPipeline;")]
    [InlineData("[ResiliencePipeline<int>] [Retry(ShouldHandle = \"Missing\")] public sealed partial class MissingHook;")]
    public void AShapeTheGeneratorCannotHandle_GeneratesNothing(string fixture)
    {
        var run = Run(fixture);

        run.GeneratedSources.Should().BeEmpty("the analyzers (MPR0004–MPR0007) report such a class; the generator emits nothing for it");
    }

    [Fact]
    public void OneFile_HoldsEveryPipeline()
    {
        var run = Run("""
            [ResiliencePipeline<int>] [Retry] public sealed partial class A;
            [ResiliencePipeline<int>] [Retry] public sealed partial class B;
            """);

        run.GeneratedSources.Select(s => s.HintName).Should().Equal(["ResiliencePipelines.g.cs"]);
        run.AllSources.Should().Contain("partial class A :");
        run.AllSources.Should().Contain("partial class B :");
    }
}
