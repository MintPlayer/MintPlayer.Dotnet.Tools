using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using MintPlayer.AsyncPipeline;

namespace MintPlayer.AsyncPipeline.Tests;

/// <summary>
/// The pipeline starts its consumer tasks in the constructor and completes the output
/// writer as a side effect of being awaited, so every test awaits the pipeline before
/// draining its output — otherwise ReadAllAsync would never terminate.
///
/// consumerCount stays 1 unless a test is specifically about concurrency: with more
/// consumers the interleaving is genuinely nondeterministic, so only set-based
/// assertions are safe there.
/// </summary>
public class PipelineTests
{
    [Fact]
    public async Task Create_ProducesEveryEmittedValue()
    {
        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            output.Writer.TryWrite(pageNumber);
            return Task.FromResult(pageNumber < 4);
        });

        await pipeline;

        (await pipeline.DrainAsync()).Should().Equal([0, 1, 2, 3, 4]);
    }

    [Fact]
    public async Task Create_WhenTheActionImmediatelyReportsNoMore_RunsExactlyOnce()
    {
        var invocations = 0;

        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            invocations++;
            output.Writer.TryWrite(42);
            return Task.FromResult(false);
        });

        await pipeline;

        invocations.Should().Be(1);
        (await pipeline.DrainAsync()).Should().Equal([42]);
    }

    [Fact]
    public async Task Create_PassesTheConsumerCountToTheAction()
    {
        var seen = -1;

        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            seen = consumerCount;
            return Task.FromResult(false);
        }, consumerCount: 1);

        await pipeline;

        seen.Should().Be(1);
    }

    [Fact]
    public async Task Create_IncrementsThePageNumberOnEachIteration()
    {
        var pages = new List<int>();

        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            pages.Add(pageNumber);
            return Task.FromResult(pageNumber < 3);
        });

        await pipeline;

        pages.Should().Equal([0, 1, 2, 3]);
    }

    [Fact]
    public async Task Create_EmitsNothing_WhenTheActionWritesNothing()
    {
        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) => Task.FromResult(false));

        await pipeline;

        (await pipeline.DrainAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_SupportsAnAsyncAction()
    {
        var pipeline = Pipeline<string>.Create(async (pageNumber, consumerCount, output) =>
        {
            await Task.Delay(10);
            output.Writer.TryWrite($"page{pageNumber}");
            return pageNumber < 1;
        });

        await pipeline;

        (await pipeline.DrainAsync()).Should().Equal(["page0", "page1"]);
    }

    [Fact]
    public async Task Concat_TransformsEveryValue()
    {
        var source = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            output.Writer.TryWrite(pageNumber);
            return Task.FromResult(pageNumber < 2);
        });

        var doubled = source.Concat<string>(async (pageNumber, consumerCount, input, output) =>
        {
            if (await input.Reader.WaitToReadAsync())
            {
                while (input.Reader.TryRead(out var value))
                    output.Writer.TryWrite($"v{value * 2}");
                return true;
            }

            return false;
        });

        await doubled;

        (await doubled.DrainAsync()).Should().BeEquivalentTo(["v0", "v2", "v4"]);
    }

    [Fact]
    public async Task Concat_CanBeChainedThreeDeep()
    {
        var stage1 = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            output.Writer.TryWrite(pageNumber);
            return Task.FromResult(pageNumber < 2);
        });

        var stage2 = stage1.Concat<int>(Forward<int, int>(v => v * 10));
        var stage3 = stage2.Concat<string>(Forward<int, string>(v => $"#{v}"));

        await stage3;

        (await stage3.DrainAsync()).Should().BeEquivalentTo(["#0", "#10", "#20"]);
    }

    [Fact]
    public async Task AwaitingTheOutermostStage_AlsoAwaitsTheInnerOne()
    {
        var innerFinished = false;

        var source = Pipeline<int>.Create(async (pageNumber, consumerCount, output) =>
        {
            await Task.Delay(30);
            output.Writer.TryWrite(1);
            innerFinished = true;
            return false;
        });

        var outer = source.Concat<int>(Forward<int, int>(v => v));

        await outer;

        innerFinished.Should().BeTrue();
        (await outer.DrainAsync()).Should().Equal([1]);
    }

    [Fact]
    public async Task MultipleConsumers_EachRunTheAction()
    {
        var invocations = new ConcurrentBag<int>();

        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            invocations.Add(pageNumber);
            output.Writer.TryWrite(pageNumber);
            return Task.FromResult(false);
        }, consumerCount: 4);

        await pipeline;

        // Four consumers, each doing exactly one pass, so four invocations of page 0.
        invocations.Should().HaveCount(4);
        (await pipeline.DrainAsync()).Should().HaveCount(4);
    }

    [Fact]
    public void CanAwait_IsAlwaysTrue()
        => Pipeline<int>.Create((pageNumber, consumerCount, output) => Task.FromResult(false))
            .CanAwait.Should().BeTrue();

    [Fact]
    public async Task AnExceptionInTheAction_SurfacesOnAwait()
    {
        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output)
            => throw new InvalidOperationException("boom"));

        var act = async () => await pipeline;

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");
    }

    [Fact]
    public async Task AwaitingTwice_IsSafe()
    {
        var pipeline = Pipeline<int>.Create((pageNumber, consumerCount, output) =>
        {
            output.Writer.TryWrite(1);
            return Task.FromResult(false);
        });

        await pipeline;
        // Completing an already-completed writer must not throw out of GetAwaiter's
        // continuation, which is what a second await exercises.
        await pipeline;
    }

    /// <summary>
    /// A stage body that forwards everything from the input channel through a projection.
    /// Written once because the WaitToReadAsync/TryRead shape is boilerplate that would
    /// otherwise be repeated in every Concat test.
    /// </summary>
    private static Func<int, int, Channel<TIn>, Channel<TOut>, Task<bool>> Forward<TIn, TOut>(Func<TIn, TOut> project)
        => async (pageNumber, consumerCount, input, output) =>
        {
            if (await input.Reader.WaitToReadAsync())
            {
                while (input.Reader.TryRead(out var value))
                    output.Writer.TryWrite(project(value));
                return true;
            }

            return false;
        };
}

internal static class PipelineTestExtensions
{
    /// <summary>
    /// Reads everything the pipeline produced. The output channel is protected, and there
    /// is no public reader on Pipeline, so this reaches it reflectively — deliberately
    /// confined to this one helper rather than spread through the tests.
    ///
    /// Only valid after awaiting the pipeline: that is what completes the writer.
    /// </summary>
    public static async Task<List<TOut>> DrainAsync<TIn, TOut>(this Pipeline<TIn, TOut> pipeline)
    {
        var field = typeof(Pipeline<TIn, TOut>)
            .GetField("output", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Pipeline<,>.output is gone; update this helper.");

        var channel = (Channel<TOut>)field.GetValue(pipeline)!;

        var items = new List<TOut>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);

        return items;
    }
}
