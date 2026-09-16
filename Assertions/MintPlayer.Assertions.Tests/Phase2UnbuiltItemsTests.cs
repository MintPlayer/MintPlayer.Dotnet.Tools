using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The four items the Phase 2 plan listed under M3/M4 as built and which a grep proved were not:
/// <c>TaskCompletionSource</c> assertions, proper subset/superset, comparer-lambda overloads on
/// <c>Equal</c>/<c>StartWith</c>/<c>EndWith</c>, and the string <see cref="StringMatchOptions"/>
/// overloads.
/// </summary>
public class Phase2UnbuiltItemsTests
{
    private static readonly int[] Numbers = [1, 2, 3];

    #region Proper subset / superset

    [Fact]
    public void BeProperSubsetOf_PassesWhenSupersetHasMore()
    {
        Numbers.Should().BeProperSubsetOf([1, 2, 3, 4]);
    }

    /// <summary>
    /// The distinction from <c>BeSubsetOf</c>: equal sets are subsets, but not <i>proper</i> ones.
    /// </summary>
    [Fact]
    public void BeProperSubsetOf_FailsWhenTheSetsAreEqual()
    {
        Numbers.Should().BeSubsetOf([1, 2, 3]);

        var ex = Record.Exception(() => Numbers.Should().BeProperSubsetOf([1, 2, 3]));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("same items", ex!.Message);
    }

    [Fact]
    public void BeProperSubsetOf_FailsAndNamesTheItemsOutsideTheSuperset()
    {
        var ex = Record.Exception(() => Numbers.Should().BeProperSubsetOf([1, 9]));

        Assert.Contains("2", ex!.Message);
        Assert.Contains("3", ex.Message);
    }

    /// <summary>
    /// Set semantics: duplicates do not make a subset proper, because both sides are the same set.
    /// </summary>
    [Fact]
    public void BeProperSubsetOf_IgnoresDuplicates()
    {
        int[] withDuplicates = [1, 1, 2];

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => withDuplicates.Should().BeProperSubsetOf([1, 2])));
    }

    [Fact]
    public void BeProperSupersetOf_PassesWhenTheCollectionHasMore()
    {
        Numbers.Should().BeProperSupersetOf([1, 2]);
    }

    [Fact]
    public void BeProperSupersetOf_FailsWhenTheSetsAreEqual()
    {
        Numbers.Should().BeSupersetOf([1, 2, 3]);

        var ex = Record.Exception(() => Numbers.Should().BeProperSupersetOf([1, 2, 3]));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("same items", ex!.Message);
    }

    [Fact]
    public void BeProperSupersetOf_FailsWhenAnItemIsMissing()
    {
        var ex = Record.Exception(() => Numbers.Should().BeProperSupersetOf([1, 9]));

        Assert.Contains("missing", ex!.Message);
        Assert.Contains("9", ex.Message);
    }

    #endregion

    #region Comparer-lambda overloads

    private sealed record Order(int Id, string Name);

    private sealed record OrderDto(int Id);

    [Fact]
    public void Equal_ComparesTwoDifferentlyShapedSequences()
    {
        Order[] orders = [new(1, "a"), new(2, "b")];
        OrderDto[] dtos = [new(1), new(2)];

        orders.Should().Equal(dtos, (o, d) => o.Id == d.Id);
    }

    [Fact]
    public void Equal_WithComparer_ReportsTheFirstDifferingIndex()
    {
        Order[] orders = [new(1, "a"), new(2, "b")];
        OrderDto[] dtos = [new(1), new(9)];

        var ex = Record.Exception(() => orders.Should().Equal(dtos, (o, d) => o.Id == d.Id));

        Assert.Contains("index 1", ex!.Message);
    }

    [Fact]
    public void Equal_WithComparer_FailsOnALengthMismatch()
    {
        Order[] orders = [new(1, "a")];
        OrderDto[] dtos = [new(1), new(2)];

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => orders.Should().Equal(dtos, (o, d) => o.Id == d.Id)));
    }

    [Fact]
    public void StartWith_AndEndWith_TakeAComparer()
    {
        Order[] orders = [new(1, "a"), new(2, "b"), new(3, "c")];

        orders.Should().StartWith([new OrderDto(1)], (o, d) => o.Id == d.Id);
        orders.Should().EndWith([new OrderDto(3)], (o, d) => o.Id == d.Id);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => orders.Should().StartWith([new OrderDto(3)], (o, d) => o.Id == d.Id)));
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => orders.Should().EndWith([new OrderDto(1)], (o, d) => o.Id == d.Id)));
    }

    /// <summary>
    /// The ordinal overloads must keep binding as before — the comparer forms take a different second
    /// parameter, so neither hijacks the other.
    /// </summary>
    [Fact]
    public void TheOrdinalOverloadsStillBind()
    {
        Numbers.Should().Equal([1, 2, 3]);
        Numbers.Should().Equal([1, 2, 3], "the values are fixed");
        Numbers.Should().StartWith(1);
        Numbers.Should().EndWith(3);
    }

    #endregion

    #region String match options

    [Fact]
    public void Be_IgnoringCase()
    {
        "Hello".Should().Be("hello", StringMatchOptions.IgnoringCase);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => "Hello".Should().Be("hello", StringMatchOptions.None)));
    }

    [Fact]
    public void Be_IgnoringSurroundingWhitespace()
    {
        "  hello  ".Should().Be("hello", StringMatchOptions.IgnoringSurroundingWhitespace);
        "  hello".Should().Be("hello", StringMatchOptions.IgnoringLeadingWhitespace);
        "hello  ".Should().Be("hello", StringMatchOptions.IgnoringTrailingWhitespace);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => "  hello".Should().Be("hello", StringMatchOptions.IgnoringTrailingWhitespace)));
    }

    [Fact]
    public void Be_IgnoringAllWhitespace_CollapsesInteriorRunsToo()
    {
        "a b\tc".Should().Be("abc", StringMatchOptions.IgnoringAllWhitespace);
    }

    /// <summary>
    /// The case the flag exists for: generated output against a checked-in literal, in a repository
    /// that does not normalise line endings.
    /// </summary>
    [Fact]
    public void Be_IgnoringNewlineStyle()
    {
        "a\r\nb\rc".Should().Be("a\nb\nc", StringMatchOptions.IgnoringNewlineStyle);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => "a\r\nb".Should().Be("a\nb", StringMatchOptions.None)));
    }

    /// <summary>
    /// A bare <c>\r</c> must become one <c>\n</c>, not two — the bug a naive pair of
    /// <c>string.Replace</c> calls produces.
    /// </summary>
    [Fact]
    public void Be_IgnoringNewlineStyle_DoesNotDoubleUpCrLf()
    {
        "a\r\nb".Should().Be("a\nb", StringMatchOptions.IgnoringNewlineStyle);
        "a\r\n\r\nb".Should().Be("a\n\nb", StringMatchOptions.IgnoringNewlineStyle);
    }

    [Fact]
    public void FlagsCombine()
    {
        " A\r\nB ".Should().Be("a\nb", StringMatchOptions.IgnoringCase | StringMatchOptions.IgnoringNewlineStyle | StringMatchOptions.IgnoringSurroundingWhitespace);
    }

    [Fact]
    public void NotBe_WithOptions()
    {
        "Hello".Should().NotBe("world", StringMatchOptions.IgnoringCase);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => "Hello".Should().NotBe("hello", StringMatchOptions.IgnoringCase)));
    }

    [Fact]
    public void StartWith_EndWith_Contain_WithOptions()
    {
        "Hello World".Should().StartWith("hello", StringMatchOptions.IgnoringCase);
        "Hello World".Should().EndWith("WORLD", StringMatchOptions.IgnoringCase);
        "Hello World".Should().Contain("LO WO", StringMatchOptions.IgnoringCase);

        "Hello World".Should().NotStartWith("world", StringMatchOptions.IgnoringCase);
        "Hello World".Should().NotEndWith("hello", StringMatchOptions.IgnoringCase);
        "Hello World".Should().NotContain("xyz", StringMatchOptions.IgnoringCase);
    }

    /// <summary>
    /// <c>IgnoringAllWhitespace</c> would reduce <c>" "</c> to the empty string, which every subject
    /// contains. That has to be rejected rather than passing vacuously.
    /// </summary>
    [Fact]
    public void Contain_RejectsAnExpectationThatNormalisesToNothing()
    {
        Assert.Throws<ArgumentException>(
            () => "hello".Should().Contain(" ", StringMatchOptions.IgnoringAllWhitespace));
    }

    /// <summary>The failure message names the options, so a "why did this fail" read is possible.</summary>
    [Fact]
    public void TheFailureMessageNamesTheOptions()
    {
        var ex = Record.Exception(() => "hello".Should().Be("world", StringMatchOptions.IgnoringCase));

        Assert.Contains("IgnoringCase", ex!.Message);
    }

    [Fact]
    public void TheOrdinalStringOverloadsStillBind()
    {
        "hello".Should().Be("hello");
        "hello".Should().Be("hello", "it is fixed");
        "hello".Should().StartWith("he");
        "hello".Should().Contain("ell", "it is in the middle");
    }

    #endregion

    #region TaskCompletionSource

    [Fact]
    public async Task CompleteWithinAsync_PassesWhenSomethingSetsIt()
    {
        var tcs = new TaskCompletionSource();
        _ = Task.Run(tcs.SetResult);

        await tcs.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CompleteWithinAsync_FailsWhenNothingSetsIt()
    {
        var tcs = new TaskCompletionSource();

        var ex = await Record.ExceptionAsync(() => tcs.Should().CompleteWithinAsync(TimeSpan.FromMilliseconds(20)));

        Assert.IsType<AssertionFailedException>(ex);
    }

    /// <summary>
    /// A faulted source is a different problem from a timeout, and says so.
    /// </summary>
    [Fact]
    public async Task CompleteWithinAsync_ReportsAFaultRatherThanATimeout()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetException(new InvalidOperationException("boom"));

        var ex = await Record.ExceptionAsync(() => tcs.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5)));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("InvalidOperationException", ex!.Message);
        Assert.DoesNotContain("but it did not", ex.Message);
    }

    [Fact]
    public async Task NotCompleteWithinAsync_PassesWhileNothingSetsIt()
    {
        var tcs = new TaskCompletionSource();

        await tcs.Should().NotCompleteWithinAsync(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task NotCompleteWithinAsync_FailsWhenItIsAlreadySet()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();

        var ex = await Record.ExceptionAsync(() => tcs.Should().NotCompleteWithinAsync(TimeSpan.FromMilliseconds(20)));

        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public async Task GenericCompleteWithinAsync_HandsBackTheResult()
    {
        var tcs = new TaskCompletionSource<int>();
        _ = Task.Run(() => tcs.SetResult(42));

        var which = await tcs.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, which.Which);
        which.Which.Should().Be(42);
    }

    [Fact]
    public async Task GenericNotCompleteWithinAsync()
    {
        var tcs = new TaskCompletionSource<int>();

        await tcs.Should().NotCompleteWithinAsync(TimeSpan.FromMilliseconds(20));

        tcs.SetResult(1);
        Assert.IsType<AssertionFailedException>(
            await Record.ExceptionAsync(() => tcs.Should().NotCompleteWithinAsync(TimeSpan.FromMilliseconds(20))));
    }

    [Fact]
    public async Task ANullCompletionSourceFails()
    {
        TaskCompletionSource? tcs = null;

        Assert.IsType<AssertionFailedException>(
            await Record.ExceptionAsync(() => tcs.Should().CompleteWithinAsync(TimeSpan.FromMilliseconds(20))));
    }

    #endregion
}
