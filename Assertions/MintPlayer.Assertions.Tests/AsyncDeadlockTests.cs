using System.Collections.Concurrent;
using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Async assertions must not deadlock when a caller blocks on them from a thread that has a
/// single-threaded <see cref="SynchronizationContext"/> — a UI app, or a test helper that calls
/// <c>.Result</c>/<c>.GetAwaiter().GetResult()</c>. If any await inside the library captured the
/// context, its continuation would be queued to a thread that is blocked waiting for it, and the
/// call would hang forever. Every await in the async path uses ConfigureAwait(false); these tests
/// are what prove it, so a future await that forgets it fails here instead of hanging a user.
/// </summary>
public class AsyncDeadlockTests
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Pumps continuations on one thread only. Blocking that thread while a continuation is
    /// queued to it is the classic deadlock.
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];

        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

        public void Drain()
        {
            foreach (var (callback, state) in queue.GetConsumingEnumerable())
                callback(state);
        }
    }

    /// <summary>
    /// Runs <paramref name="blockingCall"/> on a thread carrying a single-threaded context and
    /// fails if it does not finish — so a deadlock surfaces as a failed test, never a hung run.
    /// </summary>
    private static void AssertCompletesWithoutDeadlock(Action blockingCall)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SingleThreadSynchronizationContext());
            try { blockingCall(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };

        thread.Start();

        Assert.True(thread.Join(DeadlockTimeout),
            "The async assertion deadlocked: it never completed while blocked on a single-threaded SynchronizationContext. " +
            "Some await in the async path is missing ConfigureAwait(false).");

        if (failure is not null) throw failure;
    }

    [Fact]
    public void ThrowAsync_DoesNotDeadlock()
    {
        var act = () => Task.FromException(new InvalidOperationException("boom"));

        AssertCompletesWithoutDeadlock(() =>
            act.Should().ThrowAsync<InvalidOperationException>().GetAwaiter().GetResult());
    }

    [Fact]
    public void ThrowAsync_ChainedWithMessage_DoesNotDeadlock()
    {
        var act = () => Task.FromException(new InvalidOperationException("boom"));

        AssertCompletesWithoutDeadlock(() =>
            act.Should().ThrowAsync<InvalidOperationException>()
               .WithMessage("*boom*")
               .GetAwaiter().GetResult());
    }

    [Fact]
    public void ThrowAsync_ChainedWithInnerException_DoesNotDeadlock()
    {
        var act = () => Task.FromException(
            new InvalidOperationException("outer", new ArgumentException("inner")));

        AssertCompletesWithoutDeadlock(() =>
            act.Should().ThrowAsync<InvalidOperationException>()
               .WithInnerException<InvalidOperationException, ArgumentException>()
               .GetAwaiter().GetResult());
    }

    [Fact]
    public void NotThrowAsync_DoesNotDeadlock()
    {
        var act = () => Task.CompletedTask;

        AssertCompletesWithoutDeadlock(() =>
            act.Should().NotThrowAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void CompleteWithinAsync_DoesNotDeadlock()
    {
        // Genuinely asynchronous, so the library really has to schedule continuations — but the
        // delegate itself does not capture the context (see SubjectThatCapturesContext below).
        var act = async () => await Task.Delay(10).ConfigureAwait(false);

        AssertCompletesWithoutDeadlock(() =>
            act.Should().CompleteWithinAsync(DeadlockTimeout).GetAwaiter().GetResult());
    }

    [Fact]
    public void NotThrowAfterAsync_DoesNotDeadlock()
    {
        var act = () => Task.CompletedTask;

        AssertCompletesWithoutDeadlock(() =>
            act.Should().NotThrowAfterAsync(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10))
               .GetAwaiter().GetResult());
    }

    [Fact]
    public void GenericCompleteWithinAsync_DoesNotDeadlock()
    {
        var act = async () => { await Task.Delay(10).ConfigureAwait(false); return 42; };

        AssertCompletesWithoutDeadlock(() =>
        {
            var result = act.Should().CompleteWithinAsync(DeadlockTimeout).GetAwaiter().GetResult();
            result.Which.Should().Be(42);
        });
    }

    [Fact]
    public void AwaitingHelper_DoesNotDeadlock()
    {
        var sut = new Sut();

        AssertCompletesWithoutDeadlock(() =>
            sut.Awaiting(s => s.FailAsync()).Should().ThrowAsync<InvalidOperationException>()
               .GetAwaiter().GetResult());
    }

    /// <summary>
    /// The hazard the library cannot remove: a delegate that captures the context itself. When the
    /// code under test awaits without ConfigureAwait(false), its own continuation is queued to the
    /// blocked thread, so the task never completes — and an assertion with no timeout waits for it
    /// forever. This is the caller's "sync over async" bug, not a library defect: the fix is to
    /// await the assertion, which MPA0001 already makes mandatory. Pinned here so the boundary is
    /// explicit, and so a future change that suppresses the caller's context is a deliberate one.
    /// </summary>
    [Fact]
    public void BlockingOnASubjectThatCapturesTheContext_IsTheCallersDeadlock()
    {
        var capturing = async () => { await Task.Yield(); throw new InvalidOperationException("boom"); };
        var completed = false;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SingleThreadSynchronizationContext());
            try
            {
                // ThrowAsync has no timeout of its own, so nothing bounds the wait.
                capturing.Should().ThrowAsync<InvalidOperationException>().GetAwaiter().GetResult();
            }
            catch { /* irrelevant: we are measuring whether it returns at all */ }
            completed = true;
        }) { IsBackground = true };

        thread.Start();
        thread.Join(TimeSpan.FromSeconds(3));

        Assert.False(completed,
            "Expected the caller's own context-capturing delegate to deadlock when blocked on. " +
            "If this now completes, the library began suppressing the caller's SynchronizationContext " +
            "— a deliberate behaviour change that needs documenting.");
    }

    /// <summary>
    /// CompleteWithinAsync is the exception: its timeout bounds the wait, so even a
    /// context-capturing subject fails the assertion rather than hanging the run.
    /// </summary>
    [Fact]
    public void CompleteWithinAsync_TimesOutRatherThanHanging_EvenForACapturingSubject()
    {
        var capturing = async () => await Task.Yield();
        var completed = false;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SingleThreadSynchronizationContext());
            try { capturing.Should().CompleteWithinAsync(TimeSpan.FromMilliseconds(200)).GetAwaiter().GetResult(); }
            catch (AssertionFailedException) { /* expected: it did not complete in time */ }
            completed = true;
        }) { IsBackground = true };

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)) && completed,
            "CompleteWithinAsync should fail on its timeout instead of hanging.");
    }

    /// <summary>Awaiting the same assertion, rather than blocking, never deadlocks.</summary>
    [Fact]
    public async Task AwaitingACapturingSubject_IsFine()
    {
        var capturing = async () => await Task.Yield();

        await capturing.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class Sut
    {
        public async Task FailAsync()
        {
            await Task.Delay(10).ConfigureAwait(false);
            throw new InvalidOperationException("boom");
        }
    }
}
