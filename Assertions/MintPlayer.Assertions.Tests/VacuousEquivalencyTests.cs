using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Covers the guard against a <em>vacuous</em> equivalency comparison — one in which some node
/// compares no members, so the positive form passes for any pair of values and the negative form
/// fails for any pair. Also pins the behaviour of casting to <c>object</c>, which is widely
/// believed to disable the comparison and does not.
/// </summary>
public class VacuousEquivalencyTests
{
    private sealed class Invoice
    {
        public string Name { get; set; } = "";
        public int Amount { get; set; }
    }

    /// <summary>A type with no comparable members: only private state and methods.</summary>
    private sealed class Marker
    {
        private readonly int hidden = 42;
        public int Reveal() => hidden;
    }

    // ---------------------------------------------------------------------------------------
    // The guard fires
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BeEquivalentTo_Throws_WhenExpectationTypeHasNoMembers()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(new object()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex!.Message);
        Assert.Contains("can never fail", ex.Message);
        Assert.Contains("Object", ex.Message);
        Assert.Contains("Invoice", ex.Message);
        Assert.Contains("AllowingVacuousComparison()", ex.Message);
    }

    /// <summary>
    /// The mirror guard. Without it the negative form fails with "no differences were found",
    /// which is the confusing symptom that hides the mistake instead of naming it.
    /// </summary>
    [Fact]
    public void NotBeEquivalentTo_Throws_WhenExpectationTypeHasNoMembers()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(new object()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Throws_ForEmptyAnonymousExpectation()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(new { }));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Throws_WhenExpectationTypeExposesOnlyNonPublicMembers()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(new Marker()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Marker", ex!.Message);
        Assert.Contains("no public properties or fields", ex.Message);
    }

    /// <summary>
    /// The second cause, which needs a different cure and so gets a different message: the
    /// expectation has members, but the configured options removed every one of them.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_Throws_WhenOptionsExcludeEveryMember()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };
        var expectation = new Invoice { Name = "Other", Amount = 99 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation,
            o => o.Excluding(x => x.Name).Excluding(x => x.Amount)));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("removed by the configured", ex!.Message);
        Assert.Contains("Keep at least one member", ex.Message);
        Assert.DoesNotContain("no public properties or fields", ex.Message);
    }

    [Fact]
    public void BeEquivalentTo_Throws_WhenOnlyExcludedPathsRemain()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };
        var expectation = new Invoice { Name = "Other", Amount = 99 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.ExcludingPath("*")));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex!.Message);
    }

    /// <summary>
    /// Nested vacuity: the collection lengths really are compared, so a guard that only counted
    /// whole-comparison assertions would see one real assertion and stay quiet while every element
    /// comparison asserted nothing. This is the case FluentAssertions' root-only guard misses.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_Throws_WhenNestedElementComparisonIsVacuous()
    {
        object[] subject = [new Invoice { Name = "ACME", Amount = 10 }];
        object[] expectation = [new object()];

        var ex = Record.Exception(() => ((object)subject).Should().BeEquivalentTo(expectation));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex!.Message);
        Assert.Contains("Invoice", ex.Message);
    }

    /// <summary>
    /// Excluding every member of a nested type is the idiomatic way to say "do not compare this
    /// subtree" — the package README documents exactly this for an audit-timestamp type whose
    /// only member is the timestamp. It must not be refused, even though that node ends up
    /// comparing nothing, because the assertion as a whole still asserts plenty.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_Allows_ExcludingEveryMemberOfANestedType()
    {
        var subject = new { Name = "ACME", Audit = new Audit { ModifiedOn = new DateTime(2026, 1, 1) } };
        var expectation = new { Name = "ACME", Audit = new Audit { ModifiedOn = new DateTime(1999, 9, 9) } };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingNested<Audit>(x => x.ModifiedOn));
    }

    /// <summary>
    /// The counterpart: at the <em>root</em> there is nothing else left to assert, so excluding
    /// every member is still refused. This is the line the guard draws between the two cases.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_Throws_WhenEveryMemberIsExcludedAtTheRoot()
    {
        var subject = new Audit { ModifiedOn = new DateTime(2026, 1, 1) };
        var expectation = new Audit { ModifiedOn = new DateTime(1999, 9, 9) };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation,
            o => o.ExcludingNested<Audit>(x => x.ModifiedOn)));

        Assert.IsType<InvalidOperationException>(ex);
    }

    private sealed class Audit
    {
        public DateTime ModifiedOn { get; set; }
    }

    [Fact]
    public void BeEquivalentTo_Throws_WhenNestedMemberComparisonIsVacuous()
    {
        var subject = new { Inner = new Invoice { Name = "ACME", Amount = 10 } };
        var expectation = new { Inner = (object)new object() };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Inner", ex!.Message);
    }

    // ---------------------------------------------------------------------------------------
    // The guard stays out of the way
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Two values that both expose no members really are equivalent, so this must not throw —
    /// it is the false positive that FluentAssertions is most often complained about.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_Passes_ForTwoMemberlessValues()
    {
        new object().Should().BeEquivalentTo(new object());
        new Marker().Should().BeEquivalentTo(new Marker());
    }

    [Fact]
    public void BeEquivalentTo_Passes_ForTwoEmptyCollections()
    {
        Array.Empty<Invoice>().Should().BeEquivalentTo(Array.Empty<Invoice>());
    }

    /// <summary>An empty expectation against a populated subject is caught by the length check, not the guard.</summary>
    [Fact]
    public void BeEquivalentTo_Fails_Normally_ForEmptyExpectationAgainstPopulatedSubject()
    {
        Invoice[] subject = [new() { Name = "ACME", Amount = 10 }];

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(Array.Empty<Invoice>()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("0 item(s)", ex!.Message);
    }

    /// <summary>A memberless subject against a member-bearing expectation is a real failure, not vacuity.</summary>
    [Fact]
    public void BeEquivalentTo_Fails_Normally_WhenSubjectHasNoMembers()
    {
        var ex = Record.Exception(() => new object().Should().BeEquivalentTo(new Invoice { Name = "ACME", Amount = 10 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expectation has member Name but subject does not", ex!.Message);
    }

    [Fact]
    public void AllowingVacuousComparison_OptsOutOfTheGuard()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };

        subject.Should().BeEquivalentTo(new object(), o => o.AllowingVacuousComparison());

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(
            new object(), o => o.AllowingVacuousComparison()));

        // Opted in, so the negative form now reports the ordinary "no differences" failure
        // instead of refusing the comparison.
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no differences were found", ex!.Message);
    }

    /// <summary>
    /// A root-level custom comparer replaces the member walk entirely, so it is a deliberate
    /// statement that members are not what should be compared — not a mistake to refuse.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_Passes_WhenRootCustomComparerReplacesTheMemberWalk()
    {
        var subject = new Invoice { Name = "ACME", Amount = 10 };
        var expectation = new Invoice { Name = "Other", Amount = 99 };

        subject.Should().BeEquivalentTo(expectation, o => o.Using<Invoice>((_, _) => { }));
    }

    // ---------------------------------------------------------------------------------------
    // Casting to object does not disable the comparison (issue #177's premise)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Erasing both sides to <c>object</c> is widely believed to silently disable the comparison.
    /// It does not: <c>ResolveNodeType</c> maps a declared <c>object</c> back to the runtime type,
    /// so the real members are still walked. It costs the generated accessors (see MPA0004) and
    /// makes the options lambda untypeable, but the verdict is correct.
    /// </summary>
    [Fact]
    public void BeEquivalentTo_StillComparesMembers_WhenBothSidesAreErasedToObject()
    {
        var actual = new Invoice { Name = "John", Amount = 30 };
        var expected = new Invoice { Name = "Jane", Amount = 31 };

        var ex = Record.Exception(() => ((object)actual).Should().BeEquivalentTo((object)expected));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Name", ex!.Message);
        Assert.Contains("Amount", ex.Message);
    }

    [Fact]
    public void NotBeEquivalentTo_Passes_WhenBothSidesAreErasedToObjectAndDiffer()
    {
        var actual = new Invoice { Name = "John", Amount = 30 };
        var expected = new Invoice { Name = "Jane", Amount = 31 };

        ((object)actual).Should().NotBeEquivalentTo((object)expected);
    }

    /// <summary>The control that proves the erased comparison is not simply always-failing.</summary>
    [Fact]
    public void NotBeEquivalentTo_Fails_WhenBothSidesAreErasedToObjectAndMatch()
    {
        var actual = new Invoice { Name = "John", Amount = 30 };
        var expected = new Invoice { Name = "John", Amount = 30 };

        var ex = Record.Exception(() => ((object)actual).Should().NotBeEquivalentTo((object)expected));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no differences were found", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_StillComparesItems_WhenErasedArraysDiffer()
    {
        Invoice[] actual = [new() { Name = "John", Amount = 30 }];
        Invoice[] expected = [new() { Name = "Jane", Amount = 31 }];

        var ex = Record.Exception(() => ((object)actual).Should().BeEquivalentTo((object)expected));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no equivalent item was found", ex!.Message);
    }
}
