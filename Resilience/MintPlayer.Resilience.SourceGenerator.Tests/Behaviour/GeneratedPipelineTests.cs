using System.Runtime.CompilerServices;
using MintPlayer.Resilience.CircuitBreaker;
using ResilienceSamples;
using static MintPlayer.Resilience.SourceGenerator.Tests.Behaviour.Script;

namespace MintPlayer.Resilience.SourceGenerator.Tests.Behaviour;

/// <summary>The public surface of the generated members: overloads, forms, reload, the DI seam and pooling.</summary>
[Collection(nameof(GeneratedPipelines))]
public class GeneratedPipelineTests
{
    [Fact]
    public async Task EveryOverload_RunsTheCallbackThroughThePipeline()
    {
        RetryTimeoutPipeline.UseTimeProvider(TimeProvider.System);
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            (await RetryTimeoutPipeline.ExecuteAsync(static _ => new ValueTask<int>(1))).Should().Be(1);
            (await RetryTimeoutPipeline.ExecuteAsync(static (s, _) => new ValueTask<int>(s), 2)).Should().Be(2);
            (await RetryTimeoutPipeline.ExecuteAsync(static _ => new ValueTask<int>(3), context)).Should().Be(3);
            (await RetryTimeoutPipeline.ExecuteAsync(static (_, s) => new ValueTask<int>(s), context, 4)).Should().Be(4);
            (await RetryTimeoutPipeline.TryExecuteAsync(static _ => new ValueTask<int>(5))).Result.Should().Be(5);
            (await RetryTimeoutPipeline.TryExecuteAsync(static (s, _) => new ValueTask<int>(s), 6)).Result.Should().Be(6);
            (await RetryTimeoutPipeline.TryExecuteAsync(static _ => new ValueTask<int>(7), context)).Result.Should().Be(7);
            (await RetryTimeoutPipeline.TryExecuteAsync(static (_, s) => new ValueTask<int>(s), context, 8)).Result.Should().Be(8);
            RetryTimeoutPipeline.Execute(static () => 9).Should().Be(9);
            RetryTimeoutPipeline.Execute(static _ => 10).Should().Be(10);
            RetryTimeoutPipeline.Execute(static s => s, 11).Should().Be(11);
            RetryTimeoutPipeline.Execute(static (s, _) => s, 12).Should().Be(12);
            RetryTimeoutPipeline.Execute(static _ => 13, context).Should().Be(13);
            RetryTimeoutPipeline.Execute(static (_, s) => s, context, 14).Should().Be(14);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public async Task GenericForm_ExecutesAnyResultType_AndVoid()
    {
        GenericPipeline.UseTimeProvider(TimeProvider.System);
        var calls = 0;

        (await GenericPipeline.ExecuteAsync(static _ => new ValueTask<string>("text"))).Should().Be("text");
        (await GenericPipeline.ExecuteAsync(static (s, _) => new ValueTask<int>(s), 42)).Should().Be(42);
        GenericPipeline.Execute(static () => 1.5).Should().Be(1.5);
        await GenericPipeline.ExecuteAsync(_ =>
        {
            calls++;
            return default;
        });
        GenericPipeline.Execute(() => calls++);
        GenericPipeline.Execute(static (box, _) => box.Value++, new StrongBox<int>());

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GenericForm_RetriesAVoidCallback()
    {
        GenericPipeline.UseTimeProvider(TimeProvider.System);
        var attempts = 0;

        await GenericPipeline.ExecuteAsync(_ =>
        {
            if (++attempts < 2)
            {
                throw new TimeoutException();
            }

            return default;
        });

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task TryExecuteAsync_ReturnsARejection_WithoutThrowing()
    {
        BreakerPipeline.UseTimeProvider(FakeClock.Create());
        _ = await BreakerPipeline.TryExecuteAsync(Callback, new Script(Fail));

        var outcome = await BreakerPipeline.TryExecuteAsync(Callback, new Script(Ok(1)));

        outcome.IsRejected.Should().BeTrue();
        outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
        (outcome.RetryAfter > TimeSpan.Zero).Should().BeTrue("an open circuit reports when to retry");
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsBrokenCircuitException_ForARejection()
    {
        BreakerPipeline.UseTimeProvider(FakeClock.Create());
        _ = await BreakerPipeline.TryExecuteAsync(Callback, new Script(Fail));

        Func<Task> act = async () => await BreakerPipeline.ExecuteAsync(Callback, new Script(Ok(1)));

        await act.Should().ThrowAsync<BrokenCircuitException>();
    }

    [Fact]
    public async Task UseTimeProvider_ResetsTheState()
    {
        BreakerPipeline.UseTimeProvider(FakeClock.Create());
        _ = await BreakerPipeline.TryExecuteAsync(Callback, new Script(Fail));
        (await BreakerPipeline.TryExecuteAsync(Callback, new Script(Ok(1)))).IsRejected.Should().BeTrue();

        BreakerPipeline.UseTimeProvider(FakeClock.Create());

        (await BreakerPipeline.TryExecuteAsync(Callback, new Script(Ok(1)))).Result.Should().Be(1);
    }

    [Fact]
    public async Task Reload_InvalidValues_AreRejected_AndThePreviousSnapshotStaysLive()
    {
        ReloadablePipeline.UseTimeProvider(FakeClock.Create());
        var options = new ReloadablePipelineOptions();
        options.Retry.MaxRetryAttempts = 0;

        ReloadablePipeline.TryApply(options, out var error).Should().BeFalse();
        error.Should().Contain("MaxRetryAttempts");

        var script = new Script(Fail);
        _ = await ReloadablePipeline.TryExecuteAsync(Callback, script);
        script.Attempts.Should().BeGreaterThan(1, "the attribute values (3 retries) are still live");
    }

    [Fact]
    public void Reload_AMissingSection_IsRejected()
    {
        var options = new ReloadablePipelineOptions { Attempt = null! };

        ReloadablePipeline.TryApply(options, out var error).Should().BeFalse();
        error.Should().Contain("Attempt");
    }

    [Fact]
    public async Task Reload_AnUnchangedBreakerSection_KeepsTheBreakerState()
    {
        ReloadablePipeline.UseTimeProvider(FakeClock.Create());
        var tripping = new ReloadablePipelineOptions();
        tripping.Retry.MaxRetryAttempts = 1;
        ReloadablePipeline.TryApply(tripping, out _).Should().BeTrue();
        _ = await ReloadablePipeline.TryExecuteAsync(Callback, new Script(Fail));

        var retriesChanged = new ReloadablePipelineOptions();
        retriesChanged.Retry.MaxRetryAttempts = 2;
        ReloadablePipeline.TryApply(retriesChanged, out _).Should().BeTrue();

        (await ReloadablePipeline.TryExecuteAsync(Callback, new Script(Ok(1)))).Rejection.Should().Be(RejectionKind.CircuitOpen,
            "a reload that does not change the breaker's section keeps its health: the circuit is still open");
    }

    [Fact]
    public async Task Reload_InFlightExecutions_KeepTheirSnapshot()
    {
        var time = FakeClock.Create();
        ReloadablePipeline.UseTimeProvider(time);
        var noBreaking = new ReloadablePipelineOptions();
        noBreaking.Breaker.MinimumThroughput = 1_000;
        ReloadablePipeline.TryApply(noBreaking, out _).Should().BeTrue();
        var gate = new TaskCompletionSource();
        var attempts = 0;

        var inFlight = ReloadablePipeline.ExecuteAsync(async ct =>
        {
            if (++attempts < 4)
            {
                await gate.Task;
                throw new InvalidOperationException();
            }

            return attempts;
        }).AsTask();

        var fewer = new ReloadablePipelineOptions();
        fewer.Retry.MaxRetryAttempts = 1;
        fewer.Breaker.MinimumThroughput = 1_000;
        ReloadablePipeline.TryApply(fewer, out _).Should().BeTrue();
        gate.SetResult();

        (await inFlight).Should().Be(4, "the execution started with 3 retries and keeps them");
    }

    [Fact]
    public void GeneratedInterface_ExposesTheDISeam()
    {
        Name<CatalogPipeline>().Should().Be("CatalogPipeline");
        Name<TenantPipeline>().Should().Be("Tenant");
        IsInstance<CatalogPipeline>().Should().BeFalse();
        IsInstance<OrdersPipeline>().Should().BeTrue();
        Section<ReloadablePipeline, ReloadablePipelineOptions>().Should().Be("Resilience:ReloadablePipeline");
        Section<TenantPipeline, TenantPipelineOptions>().Should().Be("Resilience:Tenant");

        static string Name<T>() where T : class, IGeneratedResiliencePipeline<T> => T.PipelineName;
        static bool IsInstance<T>() where T : class, IGeneratedResiliencePipeline<T> => T.IsInstancePipeline;
        static string Section<T, TOptions>() where T : class, IReloadableResiliencePipeline<T, TOptions> where TOptions : class, new() => T.DefaultSectionPath;
    }

    [Fact]
    public async Task Create_ResolvesTheConstructorParameters_FromTheServiceProvider()
    {
        var journal = new EventJournal();
        var services = new Services { [typeof(EventJournal)] = journal };

        var pipeline = Create<InstanceBreakerPipeline>(services);
        pipeline.UseTimeProvider(FakeClock.Create());
        _ = await pipeline.TryExecuteAsync(Callback, new Script(Fail, Ok(1)));

        pipeline.Journal.Should().BeSameAs(journal);
        journal.Take().Should().Equal(["retry attempt=0"]);
    }

    [Fact]
    public void Create_UsesDefaultValues_AndFailsForAMissingService()
    {
        var tenant = Create<TenantPipeline>(new Services { [typeof(EventJournal)] = new EventJournal() });
        tenant.Should().NotBeNull();

        Action act = () => Create<TenantPipeline>(new Services());
        act.Should().Throw<InvalidOperationException>().WithMessage("*EventJournal*journal*");
    }

    [Fact]
    public void Create_ReturnsAShell_ForTheStaticForm()
        => Create<CatalogPipeline>(new Services()).Should().NotBeNull();

    private static T Create<T>(IServiceProvider services) where T : class, IGeneratedResiliencePipeline<T> => T.Create(services);

    [Fact]
    public async Task Unpooled_ASuspendedExecution_CanBeAwaitedTwice()
    {
        var task = UnpooledPipeline.ExecuteAsync(static async _ =>
        {
            await Task.Yield();
            return 42;
        });

        (await task).Should().Be(42);
        (await task).Should().Be(42);
    }

    [Fact]
    public async Task Pooled_ASuspendedExecution_AwaitedTwice_Throws()
    {
        var task = EmptyPipeline.ExecuteAsync(static async _ =>
        {
            await Task.Yield();
            return 42;
        });

        (await task).Should().Be(42);
        var second = async () => await task;

        await second.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ManualControl_AndStateProvider_FollowTheCurrentBreaker_AcrossUseTimeProvider()
    {
        // Recreating the breaker twice: each time its attachments move to the new breaker (otherwise the state provider
        // would throw "already initialized"). The system clock, because the retry waits between the rejections.
        KitchenSinkPipeline.UseTimeProvider(FakeClock.Create());
        KitchenSinkPipeline.UseTimeProvider(TimeProvider.System);

        KitchenSinkPipeline.State.CircuitState.Should().Be(CircuitState.Closed);
        await KitchenSinkPipeline.Control.IsolateAsync();
        KitchenSinkPipeline.State.CircuitState.Should().Be(CircuitState.Isolated);

        var outcome = await KitchenSinkPipeline.TryExecuteAsync(static _ => new ValueTask<string>("ok"));
        outcome.Result.Should().Be("recovered", "the isolated circuit rejects, and the fallback replaces the rejection");

        await KitchenSinkPipeline.Control.CloseAsync();
        (await KitchenSinkPipeline.TryExecuteAsync(static _ => new ValueTask<string>("ok"))).Result.Should().Be("ok");
    }

    [Fact]
    public async Task NestedPipeline_Runs()
        => (await Outer.NestedPipeline.ExecuteAsync(static _ => throw new InvalidOperationException())).Should().Be(-1);

    [Fact]
    public async Task PrdSamples_Run()
    {
        CatalogPipeline.UseTimeProvider(TimeProvider.System);
        using var response = await CatalogPipeline.ExecuteAsync(static _ => new ValueTask<HttpResponseMessage>(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var orders = new OrdersPipeline(Microsoft.Extensions.Logging.Abstractions.NullLogger<OrdersPipeline>.Instance);
        using var created = await orders.ExecuteAsync(static _ => new ValueTask<HttpResponseMessage>(new HttpResponseMessage(System.Net.HttpStatusCode.Created)));
        created.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }

    private sealed class Services : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
