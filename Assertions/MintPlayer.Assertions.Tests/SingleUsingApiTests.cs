using MintPlayer.Assertions;   // deliberately the ONLY using in this file

namespace MintPlayer.Assertions.Tests.SingleUsing;

/// <summary>
/// A consumer should need exactly one using — <c>using MintPlayer.Assertions;</c> — for everyday
/// testing, and no ConfigureAwait or other ceremony. This file is written the way a user writes
/// tests and deliberately imports nothing else, so if any everyday feature ever starts requiring
/// a second namespace, this stops compiling.
/// </summary>
/// <remarks>
/// Authoring a *custom* assertion is the documented exception: that needs
/// <c>MintPlayer.Assertions.Execution</c> (the Assertion builder) and
/// <c>MintPlayer.Assertions.Primitives</c> (the assertions type being extended).
/// </remarks>
public class SingleUsingApiTests
{
    public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }

    public class Publisher
    {
        public event EventHandler<EventArgs>? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void EverydayAssertions()
    {
        42.Should().Be(42);
        "abc".Should().StartWith("a");
        new[] { 1, 2, 3 }.Should().HaveCount(3).And.ContainSingle(x => x == 2);
        new Dictionary<string, int> { ["a"] = 1 }.Should().ContainKey("a").Which.Should().Be(1);
        DateTime.UtcNow.Should().BeAfter(DateTime.UtcNow.AddDays(-1));
        typeof(Dto).Should().BeAClass();
    }

    [Fact]
    public void Equivalency()
        => ((object)new Dto { Id = 1, Name = "a" }).Should()
            .BeEquivalentTo(new { Id = 1, Name = "a" });

    [Fact]
    public void SoftAssertions()
    {
        using (new AssertionScope("the response"))   // needs a second using?
        {
            200.Should().Be(200);
        }
    }

    [Fact]
    public void Events()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();
        publisher.Raise();
        monitor.Raise(nameof(Publisher.Changed)).WithSender(publisher);
    }

    [Fact]
    public async Task AsyncWithNoConfigureAwaitAnywhere()
    {
        // Exactly how a consumer writes it: no ConfigureAwait, subject freely yields.
        var act = async () => { await Task.Yield(); throw new InvalidOperationException("boom"); };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*boom*");
        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*BOOM*", StringComparison.OrdinalIgnoreCase);

        var ok = async () => { await Task.Yield(); return 7; };
        await ok.Should().NotThrowAsync();
        (await ok.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10))).Which.Should().Be(7);
    }

    [Fact]
    public void Exceptions()
    {
        // `Action act = ...` rather than `var act = ...`: C# cannot infer a delegate type for a
        // throw-only lambda (CS8917). A lambda that calls something infers Action fine.
        Action act = () => throw new ArgumentNullException("order");
        act.Should().Throw<ArgumentNullException>().WithParameterName("order");
    }
}
