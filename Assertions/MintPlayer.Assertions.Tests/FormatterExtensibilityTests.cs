using MintPlayer.Assertions.Formatting;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Custom formatters and the <see cref="FormattingOptions"/> knobs.
/// </summary>
/// <remarks>
/// ⚠️ <b>Custom formatter registration is process-wide, so this class is serialised against itself
/// and every test restores in a <c>finally</c>.</b> The options are a different story: they are
/// exercised only through <c>FormattingOptions.With(...)</c>, which is thread-local, so no option
/// test can affect a test running beside it. That asymmetry is deliberate — PRD §9.9 is what happens
/// when process-wide state meets parallel test classes, and the fix there was thread-local state
/// rather than switching parallelism off.
/// </remarks>
[Collection(nameof(FormatterExtensibilityTests))]
[CollectionDefinition(nameof(FormatterExtensibilityTests), DisableParallelization = true)]
public class FormatterExtensibilityTests
{
    private class Money
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "EUR";
    }

    private sealed class Cash : Money;

    [Fact]
    public void ARegisteredFormatterReplacesTheStructuralRendering()
    {
        try
        {
            Formatter.Register<Money>(m => FormattableString.Invariant($"{m.Amount} {m.Currency}"));

            Assert.Equal("12.5 EUR", Formatter.Format(new Money { Amount = 12.5m }));
        }
        finally
        {
            Formatter.ClearCustomFormatters();
        }
    }

    [Fact]
    public void ARegisteredFormatterReachesTheFailureMessage()
    {
        try
        {
            Formatter.Register<Money>(m => FormattableString.Invariant($"{m.Amount} {m.Currency}"));
            var subject = new Money { Amount = 1m };

            var ex = Record.Exception(() => subject.Should().Be(new Money { Amount = 2m }));

            Assert.NotNull(ex);
            Assert.Contains("2 EUR", ex.Message);
        }
        finally
        {
            Formatter.ClearCustomFormatters();
        }
    }

    [Fact]
    public void AFormatterRegisteredForABaseTypeHandlesADerivedOne()
    {
        try
        {
            Formatter.Register<Money>(m => FormattableString.Invariant($"base:{m.Amount}"));

            Assert.Equal("base:3", Formatter.Format(new Cash { Amount = 3m }));
        }
        finally
        {
            Formatter.ClearCustomFormatters();
        }
    }

    [Fact]
    public void TheMostDerivedRegistrationWins()
    {
        try
        {
            Formatter.Register<Money>(m => FormattableString.Invariant($"base:{m.Amount}"));
            Formatter.Register<Cash>(c => FormattableString.Invariant($"derived:{c.Amount}"));

            Assert.Equal("derived:3", Formatter.Format(new Cash { Amount = 3m }));
            Assert.Equal("base:3", Formatter.Format(new Money { Amount = 3m }));
        }
        finally
        {
            Formatter.ClearCustomFormatters();
        }
    }

    /// <summary>
    /// A formatter is user code running on the failure path. If it throws, the assertion's own
    /// failure still has to arrive — losing the reported failure because the renderer crashed would
    /// turn a readable test failure into a mystery.
    /// </summary>
    [Fact]
    public void AThrowingFormatterDoesNotSwallowTheFailureItWasRendering()
    {
        try
        {
            Formatter.Register<Money>(_ => throw new InvalidOperationException("boom"));

            var rendered = Formatter.Format(new Money { Amount = 1m });

            Assert.Contains("threw InvalidOperationException", rendered);
        }
        finally
        {
            Formatter.ClearCustomFormatters();
        }
    }

    [Fact]
    public void UnregisterRestoresTheStructuralRendering()
    {
        try
        {
            Formatter.Register<Money>(_ => "custom");
            Assert.Equal("custom", Formatter.Format(new Money()));

            Assert.True(Formatter.Unregister<Money>());
            Assert.Contains("Amount", Formatter.Format(new Money()));

            Assert.False(Formatter.Unregister<Money>());
        }
        finally
        {
            Formatter.ClearCustomFormatters();
        }
    }

    // ---------------------------------------------------------------------------------------
    // FormattingOptions
    //
    // Every test here uses FormattingOptions.With(...), never the process-wide setters. That is the
    // point of the feature existing: the setters are global, this suite runs classes in parallel, and
    // a test that raised MaxDepth globally would change the failure messages of whatever ran beside
    // it. Same shape as PRD 9.9. The two tests that do touch the globals are the ones about the
    // globals themselves, and they restore in a finally.
    // ---------------------------------------------------------------------------------------

    private sealed class Level { public Level? Next { get; set; } public int Value { get; set; } }

    private static Level Chain(int depth)
    {
        var root = new Level { Value = 0 };
        var current = root;
        for (var i = 1; i <= depth; i++)
        {
            current.Next = new Level { Value = i };
            current = current.Next;
        }
        return root;
    }

    /// <summary>
    /// The depth marker must name the knob. "Level {…}" tells the reader their message was cut short
    /// but not that they can do anything about it, which sends them to a debugger for information the
    /// message could have carried.
    /// </summary>
    [Fact]
    public void TheDepthMarkerNamesTheOptionThatCausedIt()
    {
        var rendered = Formatter.Format(Chain(8));

        Assert.Contains("FormattingOptions.MaxDepth", rendered);
    }

    [Fact]
    public void RaisingMaxDepthShowsMore()
    {
        var shallow = Formatter.Format(Chain(8));

        using (FormattingOptions.With(maxDepth: 6))
        {
            var deep = Formatter.Format(Chain(8));
            Assert.True(deep.Length > shallow.Length, "Raising MaxDepth did not render more of the graph.");
        }
    }

    [Fact]
    public void TheCollectionMarkerNamesItsOptionToo()
    {
        using (FormattingOptions.With(maxCollectionItems: 3))
        {
            Assert.Contains("FormattingOptions.MaxCollectionItems", Formatter.Format(Enumerable.Range(0, 10).ToArray()));
        }
    }

    [Fact]
    public void MaxStringLengthTruncatesAndSaysByHowMuch()
    {
        using (FormattingOptions.With(maxStringLength: 5))
        {
            Assert.Contains("7 more chars", Formatter.Format(new string('a', 12)));
        }
    }

    [Fact]
    public void UseLineBreaksPutsEachItemOnItsOwnLine()
    {
        using (FormattingOptions.With(useLineBreaks: true))
        {
            var rendered = Formatter.Format(new[] { 1, 2, 3 });

            Assert.Contains(Environment.NewLine, rendered);
            Assert.Equal(3, rendered.Split(Environment.NewLine).Count(line => line.Trim().TrimEnd(',').Length > 0 && char.IsDigit(line.Trim()[0])));
        }
    }

    [Fact]
    public void MaxLinesCapsTheOutputAndNamesItself()
    {
        using (FormattingOptions.With(useLineBreaks: true, maxLines: 3))
        {
            Assert.Contains("FormattingOptions.MaxLines", Formatter.Format(Enumerable.Range(0, 50).ToArray()));
        }
    }

    /// <summary>The override is per thread, which is the entire reason it exists.</summary>
    /// <remarks>
    /// ⚠️ The on-thread rendering is captured BEFORE the await, and that ordering is the test, not
    /// tidiness. A thread-local override does not survive an <c>await</c> — the continuation can
    /// resume on a different thread, where it is not in effect. The first version of this test read
    /// it afterwards and failed for exactly the reason documented on <c>FormattingOptions.With</c>,
    /// which is worth leaving written down here: the caveat is easy to agree with and easy to walk
    /// straight into.
    /// </remarks>
    [Fact]
    public async Task AnOverrideDoesNotLeakToAnotherThread()
    {
        string onThisThread;
        Task<string> elsewhere;

        using (FormattingOptions.With(maxCollectionItems: 2))
        {
            onThisThread = Formatter.Format(Enumerable.Range(0, 10).ToArray());
            elsewhere = Task.Run(() => Formatter.Format(Enumerable.Range(0, 10).ToArray()));
        }

        Assert.Contains("…", onThisThread);
        Assert.DoesNotContain("…", await elsewhere);
    }

    [Fact]
    public void OverridesNest()
    {
        using (FormattingOptions.With(maxCollectionItems: 5))
        {
            Assert.Equal(5, FormattingOptions.MaxCollectionItems);

            using (FormattingOptions.With(maxCollectionItems: 2))
            {
                Assert.Equal(2, FormattingOptions.MaxCollectionItems);
            }

            Assert.Equal(5, FormattingOptions.MaxCollectionItems);
        }

        Assert.Equal(32, FormattingOptions.MaxCollectionItems);
    }

    /// <summary>An option left null keeps whatever the enclosing scope had, rather than the default.</summary>
    [Fact]
    public void AnUnsetOptionInheritsFromTheEnclosingScope()
    {
        using (FormattingOptions.With(maxCollectionItems: 5, maxDepth: 7))
        using (FormattingOptions.With(maxCollectionItems: 2))
        {
            Assert.Equal(7, FormattingOptions.MaxDepth);
        }
    }

    [Fact]
    public void DisposingTwiceDoesNotPopSomeoneElsesScope()
    {
        var outer = FormattingOptions.With(maxCollectionItems: 5);
        var inner = FormattingOptions.With(maxCollectionItems: 2);

        inner.Dispose();
        inner.Dispose();

        Assert.Equal(5, FormattingOptions.MaxCollectionItems);
        outer.Dispose();
    }

    [Fact]
    public void AnOutOfRangeOptionIsRejectedRatherThanSilentlyClamped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FormattingOptions.With(maxDepth: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormattingOptions.With(maxCollectionItems: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormattingOptions.With(maxStringLength: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormattingOptions.With(maxLines: -1));
    }

    [Fact]
    public void ResetRestoresEveryProcessWideDefault()
    {
        try
        {
            FormattingOptions.MaxDepth = 9;
            FormattingOptions.MaxCollectionItems = 9;
            FormattingOptions.MaxStringLength = 9;
            FormattingOptions.MaxLines = 9;
            FormattingOptions.UseLineBreaks = true;

            FormattingOptions.Reset();

            Assert.Equal(3, FormattingOptions.MaxDepth);
            Assert.Equal(32, FormattingOptions.MaxCollectionItems);
            Assert.Equal(512, FormattingOptions.MaxStringLength);
            Assert.Equal(0, FormattingOptions.MaxLines);
            Assert.False(FormattingOptions.UseLineBreaks);
        }
        finally
        {
            FormattingOptions.Reset();
        }
    }

    [Fact]
    public void AProcessWideSetterIsStillRejectedWhenOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FormattingOptions.MaxDepth = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => FormattingOptions.MaxCollectionItems = 0);
    }
}
