namespace MintPlayer.Assertions.Tests;

public class AsyncAssertionsTests
{
    [Fact]
    public async Task ThrowAsync_Passes_WhenExpectedExceptionIsThrown()
    {
        Func<Task> act = () => Task.FromException(new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ThrowAsync_Passes_WhenDerivedExceptionIsThrown()
    {
        Func<Task> act = () => throw new ArgumentNullException("param");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ThrowAsync_ExposesExceptionViaWhich()
    {
        Func<Task> act = async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        };

        var assertions = await act.Should().ThrowAsync<InvalidOperationException>();

        Assert.Equal("boom", assertions.Which.Message);
    }

    [Fact]
    public async Task ThrowAsync_Fails_WhenNoExceptionIsThrown()
    {
        Func<Task> act = () => Task.CompletedTask;

        var ex = await Record.ExceptionAsync(async () => await act.Should().ThrowAsync<InvalidOperationException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Expected act to throw System.InvalidOperationException", failure.Message);
        Assert.Contains("no exception was thrown", failure.Message);
    }

    [Fact]
    public async Task ThrowAsync_Fails_WhenWrongExceptionTypeIsThrown()
    {
        Func<Task> act = () => Task.FromException(new FormatException("bad format"));

        var ex = await Record.ExceptionAsync(async () => await act.Should().ThrowAsync<InvalidOperationException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("System.FormatException", failure.Message);
        Assert.Contains("bad format", failure.Message);
    }

    [Fact]
    public async Task ThrowExactlyAsync_Passes_WhenExactExceptionTypeIsThrown()
    {
        Func<Task> act = () => Task.FromException(new ArgumentException("boom"));

        await act.Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Fact]
    public async Task ThrowExactlyAsync_Fails_WhenDerivedExceptionIsThrown()
    {
        Func<Task> act = () => Task.FromException(new ArgumentNullException("param"));

        var ex = await Record.ExceptionAsync(async () => await act.Should().ThrowExactlyAsync<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to throw exactly System.ArgumentException", failure.Message);
        Assert.Contains("System.ArgumentNullException", failure.Message);
    }

    [Fact]
    public async Task NotThrowAsync_Passes_WhenNoExceptionIsThrown()
    {
        Func<Task> act = () => Task.CompletedTask;

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotThrowAsync_Fails_WhenExceptionIsThrown()
    {
        Func<Task> act = () => Task.FromException(new InvalidOperationException("kaboom"));

        var ex = await Record.ExceptionAsync(async () => await act.Should().NotThrowAsync());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect act to throw", failure.Message);
        Assert.Contains("kaboom", failure.Message);
    }

    [Fact]
    public async Task NotThrowAfterAsync_Passes_WhenFunctionEventuallySucceeds()
    {
        var attempts = 0;
        Func<Task> act = () =>
        {
            attempts++;
            return attempts < 3
                ? Task.FromException(new InvalidOperationException("not yet"))
                : Task.CompletedTask;
        };

        await act.Should().NotThrowAfterAsync(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task NotThrowAfterAsync_Fails_WithLastException_WhenFunctionKeepsThrowing()
    {
        Func<Task> act = () => Task.FromException(new InvalidOperationException("still failing"));

        var ex = await Record.ExceptionAsync(() =>
            act.Should().NotThrowAfterAsync(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("still failing", failure.Message);
        Assert.Contains("System.InvalidOperationException", failure.Message);
    }

    [Fact]
    public async Task CompleteWithinAsync_Passes_WhenTaskCompletesInTime()
    {
        Func<Task> act = () => Task.Delay(TimeSpan.FromMilliseconds(10));

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CompleteWithinAsync_Fails_WhenTaskIsTooSlow()
    {
        Func<Task> act = () => Task.Delay(TimeSpan.FromSeconds(30));

        var ex = await Record.ExceptionAsync(() =>
            act.Should().CompleteWithinAsync(TimeSpan.FromMilliseconds(50)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to complete within", failure.Message);
        Assert.Contains("but it did not", failure.Message);
    }

    [Fact]
    public async Task CompleteWithinAsync_Fails_WhenTaskFaults()
    {
        Func<Task> act = () => Task.FromException(new InvalidOperationException("faulted"));

        var ex = await Record.ExceptionAsync(() =>
            act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(30)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("faulted", failure.Message);
        Assert.Contains("System.InvalidOperationException", failure.Message);
    }

    [Fact]
    public async Task GenericThrowAsync_Passes_WhenExceptionIsThrown()
    {
        Func<Task<int>> act = () => Task.FromException<int>(new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GenericThrowAsync_Fails_WhenNoExceptionIsThrown()
    {
        Func<Task<int>> act = () => Task.FromResult(42);

        var ex = await Record.ExceptionAsync(async () => await act.Should().ThrowAsync<InvalidOperationException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no exception was thrown", failure.Message);
    }

    [Fact]
    public async Task GenericThrowExactlyAsync_Fails_WhenDerivedExceptionIsThrown()
    {
        Func<Task<int>> act = () => Task.FromException<int>(new ArgumentNullException("param"));

        var ex = await Record.ExceptionAsync(async () => await act.Should().ThrowExactlyAsync<ArgumentException>());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to throw exactly System.ArgumentException", failure.Message);
    }

    [Fact]
    public async Task GenericNotThrowAsync_ExposesResultViaWhich()
    {
        Func<Task<int>> act = () => Task.FromResult(42);

        var constraint = await act.Should().NotThrowAsync();

        Assert.Equal(42, constraint.Which);
    }

    [Fact]
    public async Task GenericNotThrowAsync_Fails_WhenExceptionIsThrown()
    {
        Func<Task<int>> act = () => Task.FromException<int>(new InvalidOperationException("kaboom"));

        var ex = await Record.ExceptionAsync(async () => await act.Should().NotThrowAsync());

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("kaboom", failure.Message);
    }

    [Fact]
    public async Task GenericCompleteWithinAsync_ExposesResultViaWhich()
    {
        Func<Task<int>> act = async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            return 42;
        };

        var constraint = await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(42, constraint.Which);
    }

    [Fact]
    public async Task GenericCompleteWithinAsync_Fails_WhenTaskIsTooSlow()
    {
        Func<Task<int>> act = async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 42;
        };

        var ex = await Record.ExceptionAsync(() =>
            act.Should().CompleteWithinAsync(TimeSpan.FromMilliseconds(50)));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to complete within", failure.Message);
    }

    [Fact]
    public async Task Awaiting_WrapsSubjectIntoAnAssertableAsyncFunction()
    {
        var subject = "text";

        await subject.Awaiting(s => Task.FromException(new InvalidOperationException(s)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Awaiting_WithResult_WrapsSubjectIntoAnAssertableAsyncFunction()
    {
        var subject = "text";

        var constraint = await subject.Awaiting(s => Task.FromResult(s.Length)).Should().NotThrowAsync();

        Assert.Equal(4, constraint.Which);
    }
}
