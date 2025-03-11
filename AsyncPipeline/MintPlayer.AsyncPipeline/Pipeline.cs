using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MintPlayer.AsyncPipeline;

/// <summary>
/// Base class to pipeline async Tasks
/// </summary>
public abstract class Pipeline
{
    /// <summary>
    /// Base pipeline on which this pipeline was concatenated
    /// </summary>
    protected internal Pipeline? inner;
}

/// <summary>
/// Pipeline which only produces output values
/// </summary>
/// <typeparam name="Tout">Type of the values that are returned by the <see cref="Func{Task}"/> </typeparam>
public class Pipeline<Tout> : Pipeline<object, Tout>
{
    /// <summary>
    /// Creates a new pipeline from a task that emits values
    /// </summary>
    /// <param name="action">Task that emits values</param>
    /// <param name="consumerCount">Number of handlers that run concurrently</param>
    /// <returns>Whether or not the method should be run again</returns>
    public static Pipeline<object, Tout> Create(Func<int, int, Channel<Tout>, Task<bool>> action, int consumerCount = 1)
    {
        var result = Pipeline<object, Tout>.Create(
            (pageNumber, consumerId, _, output) => action(pageNumber, consumerCount, output),
            null,
            consumerCount);
        return result;
    }

    /// <summary>
    /// Transforms the output of this pipeline through a task that can be run concurrently
    /// </summary>
    /// <typeparam name="Tout2">Result of the <paramref name="action"/></typeparam>
    /// <param name="action">Transformation function</param>
    /// <param name="consumerCount">Number of concurrent handlers</param>
    /// <returns>The pipeline</returns>
    public override Pipeline<Tout, Tout2> Concat<Tout2>(Func<int, int, Channel<Tout>, Channel<Tout2>, Task<bool>> action, int consumerCount = 1)
    {
        var result = Pipeline<Tout, Tout2>.Create(
            (pageNumber, consumerId, input, output) => action(pageNumber, consumerCount, input!, output),
            output, // This output is the input of the next pipeline
            consumerCount);

        result.inner = this;

        return result;
    }

    internal Pipeline(Func<int, int, Channel<Tout>, Task<bool>> action, Channel<object>? input, int consumerCount) : base((pageNumber, consumerId, input, output) => action(pageNumber, consumerId, output), input, consumerCount)
    {
    }
}

/// <summary>
/// Pipeline which receives values and produces values
/// </summary>
/// <typeparam name="Tin">Type of the values that are passed into this pipeline.</typeparam>
/// <typeparam name="Tout">Type of the values that are returned by the <see cref="Func{Task}"/> </typeparam>
public class Pipeline<Tin, Tout> : Pipeline
{
    protected readonly Channel<Tout> output = Channel.CreateUnbounded<Tout>();
    protected readonly Func<int, int, Channel<Tin>?, Channel<Tout>, Task<bool>> action;
    protected readonly Channel<Tin>? input;
    protected readonly int consumerCount;

    private readonly Task[] tasks;

    protected Pipeline(Func<int, int, Channel<Tin>?, Channel<Tout>, Task<bool>> action, Channel<Tin>? input, int consumerCount)
    {
        this.action = action;
        this.input = input;
        this.consumerCount = consumerCount;
        this.tasks = Enumerable.Range(0, consumerCount)
            .Select(consumerId => Task.Run(async () =>
            {
                var hasMore = true;
                var i = 0;
                do
                {
                    hasMore = await action(i, consumerId, input, output);
                    i++;
                }
                while (hasMore);
            }))
            .ToArray();
    }

    //public static Pipeline<TOUT> Create<TOUT>(Func<int, int, Channel<Tin>?, Channel<Tout>, Task<bool>> action, int consumerCount = 1)
    //{
    //    return Create<Tin, Tout>()
    //}

    internal static Pipeline<Tin, Tout> Create(Func<int, int, Channel<Tin>?, Channel<Tout>, Task<bool>> action, Channel<Tin>? input = null, int consumerCount = 1)
    {
        return new Pipeline<Tin, Tout>(action, input, consumerCount);
    }

    /// <summary>
    /// Transform each result of previous pipeline
    /// </summary>
    /// <typeparam name="Tout2">Type of the new output</typeparam>
    /// <param name="action">Transformation function</param>
    /// <param name="consumerCount">Number of concurrent handlers</param>
    /// <returns>The pipeline</returns>
    public virtual Pipeline<Tout, Tout2> Concat<Tout2>(Func<int, int, Channel<Tout>, Channel<Tout2>, Task<bool>> action, int consumerCount = 1)
    {
        var result = Pipeline<Tout, Tout2>.Create(
            (pageNumber, consumerId, input, output) => action(pageNumber, consumerCount, input!, output),
            output, // This output is the input of the next pipeline
            consumerCount);
        result.inner = this;
        return result;
    }

    public TaskAwaiter GetAwaiter()
    {
        return Task.WhenAll(tasks)
            .ContinueWith(_ => output.Writer.Complete())
            .GetAwaiter();
    }
}