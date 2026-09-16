using MintPlayer.Assertions.Formatting;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The failure-path features from Phase 2 M6 (PRD §5): a formatter seam, configurable rendering,
/// multi-line graph output, scope inspection, and a plug-in point for the test framework's own
/// exception type.
/// </summary>
/// <remarks>
/// Every one of these is free under §0 by construction — <c>FailWith</c> returns before rendering
/// anything when the condition holds, so none of it runs in a green suite.
/// </remarks>
public class Phase2FailurePathTests
{
    private sealed class Money
    {
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "EUR";
    }

    private sealed class MoneyFormatter : IValueFormatter
    {
        public bool CanFormat(object value) => value is Money;

        public string Format(object value, Func<object?, string> formatChild)
        {
            var money = (Money)value;
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{money.Amount} {money.Currency}");
        }
    }

    #region The formatter seam

    [Fact]
    public void AScopedFormatterReplacesTheBuiltInRendering()
    {
        var money = new Money { Amount = 12.5m, Currency = "USD" };

        var ex = Record.Exception(() =>
        {
            using var scope = new AssertionScope().Using(new MoneyFormatter());
            money.Should().BeNull();
        });

        Assert.Contains("12.5 USD", ex!.Message);
    }

    /// <summary>A formatter registered for a scope must not survive it.</summary>
    [Fact]
    public void AScopedFormatterDoesNotLeak()
    {
        var money = new Money { Amount = 1m };

        using (new AssertionScope().Using(new MoneyFormatter()))
        {
            // Nothing asserted: the scope exists only to register and then close.
        }

        var ex = Record.Exception(() => money.Should().BeNull());

        Assert.DoesNotContain("1 EUR", ex!.Message);
        Assert.Contains("Money", ex.Message);
    }

    [Fact]
    public void AGlobalFormatterAppliesUntilItIsUnregistered()
    {
        var formatter = new MoneyFormatter();
        Formatter.Register(formatter);
        try
        {
            var ex = Record.Exception(() => new Money { Amount = 3m }.Should().BeNull());
            Assert.Contains("3 EUR", ex!.Message);
        }
        finally
        {
            Assert.True(Formatter.Unregister(formatter));
        }

        var after = Record.Exception(() => new Money { Amount = 3m }.Should().BeNull());
        Assert.DoesNotContain("3 EUR", after!.Message);
    }

    private sealed class ThrowingFormatter : IValueFormatter
    {
        public bool CanFormat(object value) => throw new InvalidOperationException("boom");
        public string Format(object value, Func<object?, string> formatChild) => "";
    }

    /// <summary>
    /// A broken formatter must not take the failure message down with it: the caller is already
    /// looking at a failing assertion, and that is the worst possible moment to lose the explanation.
    /// </summary>
    [Fact]
    public void ABrokenFormatterIsSkipped()
    {
        using var scope = new AssertionScope().Using(new ThrowingFormatter());
        Assert.Equal("\"text\"", Formatter.Format("text"));
    }

    [Fact]
    public void AFormatterCanRenderItsChildrenThroughThePipeline()
    {
        var nested = new NestedFormatter();
        using var scope = new AssertionScope().Using(nested);

        Assert.Equal("wrapped(\"inner\")", Formatter.Format(new Wrapper("inner")));
    }

    private sealed record Wrapper(string Inner);

    private sealed class NestedFormatter : IValueFormatter
    {
        public bool CanFormat(object value) => value is Wrapper;
        public string Format(object value, Func<object?, string> formatChild)
            => $"wrapped({formatChild(((Wrapper)value).Inner)})";
    }

    #endregion

    #region Formatting options

    private sealed class Deep
    {
        public string Name { get; init; } = "";
        public Deep? Child { get; init; }
    }

    private static Deep Nest(int levels)
    {
        Deep? node = null;
        for (var i = levels; i > 0; i--) node = new Deep { Name = $"level{i}", Child = node };
        return node!;
    }

    /// <summary>
    /// The old elision said "Deep {…}" and left the reader guessing. The depth limit is the single
    /// most common reason a message does not show the member that actually differs, so it now names
    /// the knob that lifts it.
    /// </summary>
    [Fact]
    public void TheDepthLimitNamesItself()
    {
        var text = Formatter.Format(Nest(8));

        Assert.Contains("FormattingOptions.MaxDepth", text);
    }

    [Fact]
    public void RaisingMaxDepthShowsTheDeeperMembers()
    {
        var deep = Formatter.Format(Nest(8), FormattingOptions.Default with { MaxDepth = 10 });

        Assert.Contains("level8", deep);
        Assert.DoesNotContain("MaxDepth", deep);
    }

    [Fact]
    public void AScopeCanOverrideTheFormattingOptions()
    {
        using var scope = new AssertionScope().WithFormatting(o => o with { MaxDepth = 10 });

        Assert.Contains("level8", Formatter.Format(Nest(8)));
    }

    [Fact]
    public void MaxStringLengthAndMaxEnumerableItemsAreConfigurable()
    {
        var options = FormattingOptions.Default with { MaxStringLength = 4, MaxEnumerableItems = 2 };

        Assert.Contains("more chars", Formatter.Format("abcdefgh", options));
        Assert.Contains("…", Formatter.Format(new[] { 1, 2, 3, 4 }, options));
    }

    [Fact]
    public void UseLineBreaks_RendersAGraphOverSeveralIndentedLines()
    {
        var options = FormattingOptions.Default with { UseLineBreaks = true, MaxDepth = 10 };

        var text = Formatter.Format(Nest(3), options);
        var lines = text.Split(Environment.NewLine);

        Assert.True(lines.Length > 3, $"expected a multi-line rendering, got: {text}");
        Assert.Contains(lines, l => l.StartsWith("    ", StringComparison.Ordinal));
    }

    [Fact]
    public void UseLineBreaks_IsOffByDefaultSoExistingMessagesKeepTheirShape()
    {
        Assert.DoesNotContain(Environment.NewLine, Formatter.Format(Nest(3)));
    }

    [Fact]
    public void MaxLines_CutsARunawayRendering()
    {
        var options = FormattingOptions.Default with { UseLineBreaks = true, MaxDepth = 20, MaxLines = 5 };

        var text = Formatter.Format(Nest(20), options);

        Assert.Contains("more line(s); raise FormattingOptions.MaxLines", text);
    }

    #endregion

    #region Scope inspection

    [Fact]
    public void Discard_TakesTheFailuresAndStopsTheScopeThrowingForThem()
    {
        using var scope = new AssertionScope();

        "a".Should().Be("b");
        var discarded = scope.Discard();

        Assert.Single(discarded);
        Assert.Contains("\"b\"", discarded[0]);
        Assert.False(scope.HasFailures);
        // Disposing now throws nothing, which is the whole point: the probe was absorbed.
    }

    [Fact]
    public void Discard_OnACleanScopeReturnsNothing()
    {
        using var scope = new AssertionScope();
        Assert.Empty(scope.Discard());
    }

    [Fact]
    public void AddPreFormattedFailure_GoesInVerbatim()
    {
        var ex = Record.Exception(() =>
        {
            using var scope = new AssertionScope();
            scope.AddPreFormattedFailure("a message with {braces} and\nnewlines");
        });

        Assert.Contains("{braces}", ex!.Message);
        Assert.Contains("newlines", ex.Message);
    }

    [Fact]
    public void AReportableIsAppendedWhenTheScopeFails()
    {
        var ex = Record.Exception(() =>
        {
            using var scope = new AssertionScope();
            scope.AddReportable("request", "POST /orders");
            "a".Should().Be("b");
        });

        Assert.Contains("With request:", ex!.Message);
        Assert.Contains("POST /orders", ex.Message);
    }

    /// <summary>
    /// The reason the lazy overload exists: a reportable is usually expensive to produce and
    /// pointless when everything passes.
    /// </summary>
    [Fact]
    public void ALazyReportableIsNotProducedWhenTheScopePasses()
    {
        var produced = false;

        using (var scope = new AssertionScope())
        {
            scope.AddReportable("expensive", () => { produced = true; return "…"; });
            "a".Should().Be("a");
        }

        Assert.False(produced);
    }

    [Fact]
    public void AReportableFromANestedScopeReachesTheMessage()
    {
        var ex = Record.Exception(() =>
        {
            using var outer = new AssertionScope();
            using (var inner = new AssertionScope("inner"))
            {
                inner.AddReportable("context", "from the inner scope");
                "a".Should().Be("b");
            }
        });

        Assert.Contains("from the inner scope", ex!.Message);
    }

    [Fact]
    public void AThrowingReportableDoesNotReplaceTheFailure()
    {
        var ex = Record.Exception(() =>
        {
            using var scope = new AssertionScope();
            scope.AddReportable("broken", () => throw new InvalidOperationException("boom"));
            "a".Should().Be("b");
        });

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("\"b\"", ex!.Message);
        Assert.Contains("threw InvalidOperationException", ex.Message);
    }

    #endregion

    #region The exception factory

    private sealed class CustomFailure(string message) : Exception(message);

    [Fact]
    public void TheExceptionTypeIsPluggable()
    {
        var original = AssertionConfiguration.ExceptionFactory;
        AssertionConfiguration.ExceptionFactory = message => new CustomFailure(message);
        try
        {
            var ex = Record.Exception(() => "a".Should().Be("b"));
            Assert.IsType<CustomFailure>(ex);
        }
        finally
        {
            AssertionConfiguration.ExceptionFactory = original;
        }

        Assert.IsType<AssertionFailedException>(Record.Exception(() => "a".Should().Be("b")));
    }

    [Fact]
    public void ABrokenExceptionFactoryFallsBackWithoutLosingTheMessage()
    {
        var original = AssertionConfiguration.ExceptionFactory;
        AssertionConfiguration.ExceptionFactory = _ => throw new InvalidOperationException("boom");
        try
        {
            var ex = Record.Exception(() => "a".Should().Be("b"));

            Assert.IsType<AssertionFailedException>(ex);
            Assert.Contains("\"b\"", ex!.Message);
            Assert.Contains("ExceptionFactory threw", ex.Message);
        }
        finally
        {
            AssertionConfiguration.ExceptionFactory = original;
        }
    }

    #endregion
}
