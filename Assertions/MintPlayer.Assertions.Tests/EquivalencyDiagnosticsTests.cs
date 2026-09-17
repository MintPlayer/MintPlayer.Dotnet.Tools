namespace MintPlayer.Assertions.Tests;

/// <summary>A small graph with a known shape, so the reported counts mean something.</summary>
[AssertEquivalency]
public class DiagnosedOrder
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// <c>WithDiagnostics()</c> — the option that answers "did the comparison even look at that?" and
/// "why is this slow?" from the failure message instead of from a debugger.
/// </summary>
public class EquivalencyDiagnosticsTests
{
    [Fact]
    public void TheSummaryIsAbsentUnlessAskedFor()
    {
        var subject = new DiagnosedOrder { Id = 1, Name = "a" };
        var expectation = new DiagnosedOrder { Id = 2, Name = "a" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.NotNull(ex);
        Assert.DoesNotContain("Walk:", ex.Message);
    }

    [Fact]
    public void TheSummaryReportsTheWorkTheWalkDid()
    {
        var subject = new DiagnosedOrder { Id = 1, Name = "a" };
        var expectation = new DiagnosedOrder { Id = 2, Name = "a" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithDiagnostics()));

        Assert.NotNull(ex);
        // Root node plus one per compared member.
        Assert.Contains("Walk: 3 node(s), 2 member lookup(s), 0 collection match probe(s).", ex.Message);
    }

    [Fact]
    public void TheDifferencesAreStillReportedAlongsideTheSummary()
    {
        var subject = new DiagnosedOrder { Id = 1, Name = "a" };
        var expectation = new DiagnosedOrder { Id = 2, Name = "a" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithDiagnostics()));

        Assert.NotNull(ex);
        Assert.Contains("Id: expected 2, but found 1", ex.Message);
    }

    /// <summary>
    /// The negative form fails when it finds NO differences, which is precisely the case where "did
    /// it even look?" is the question — so the summary has to appear there too.
    /// </summary>
    [Fact]
    public void TheNegativeFormReportsTheSummaryToo()
    {
        var subject = new DiagnosedOrder { Id = 1, Name = "a" };
        var expectation = new DiagnosedOrder { Id = 1, Name = "a" };

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(expectation, o => o.WithDiagnostics()));

        Assert.NotNull(ex);
        Assert.Contains("Walk:", ex.Message);
    }

    [Fact]
    public void ProbeCountsAppearForCollections()
    {
        DiagnosedOrder[] subject = [new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" }];
        DiagnosedOrder[] expectation = [new() { Id = 1, Name = "a" }, new() { Id = 9, Name = "b" }];

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithDiagnostics()));

        Assert.NotNull(ex);
        Assert.DoesNotContain(" 0 collection match probe(s)", ex.Message);
    }

    /// <summary>
    /// Diagnostics must not survive into a comparison that did not ask for them — the counters are
    /// thread-static and a note carried on the wrong result would be worse than no note.
    /// </summary>
    [Fact]
    public void TheSummaryDoesNotLeakIntoTheNextComparison()
    {
        var subject = new DiagnosedOrder { Id = 1, Name = "a" };
        var expectation = new DiagnosedOrder { Id = 2, Name = "a" };

        Assert.NotNull(Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithDiagnostics())));

        var second = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.NotNull(second);
        Assert.DoesNotContain("Walk:", second.Message);
    }
}
