using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests;

/// <summary>A scanned type whose internal member differs between the two instances below.</summary>
[AssertEquivalency]
public class InternalMemberPoco
{
    public string Name { get; set; } = string.Empty;
    internal int Revision { get; set; }
}

/// <summary>
/// <c>IncludingInternalMembers</c> end to end: the first equivalency option answered from
/// generator-emitted <see cref="MemberTraits"/> rather than from reflection.
/// </summary>
public class IncludingInternalMembersTests
{
    private static (InternalMemberPoco Subject, InternalMemberPoco Expectation) DifferingOnlyInternally()
        => (new() { Name = "a", Revision = 1 }, new() { Name = "a", Revision = 2 });

    [Fact]
    public void InternalMembersAreNotComparedByDefault()
    {
        var (subject, expectation) = DifferingOnlyInternally();

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void InternalMembersAreComparedWhenAskedFor()
    {
        var (subject, expectation) = DifferingOnlyInternally();

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Revision", ex.Message);
    }

    [Fact]
    public void EquivalentInternalMembersStillPass()
    {
        var subject = new InternalMemberPoco { Name = "a", Revision = 7 };
        var expectation = new InternalMemberPoco { Name = "a", Revision = 7 };

        subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers());
    }

    [Fact]
    public void PublicMembersAreStillComparedWhenInternalsAreIncluded()
    {
        var subject = new InternalMemberPoco { Name = "a", Revision = 1 };
        var expectation = new InternalMemberPoco { Name = "b", Revision = 1 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Name", ex.Message);
    }

    /// <summary>
    /// The option is per comparison, not per process. A registry cache keyed only by type — an easy
    /// mistake, since the default table is cached that way — would let one call that opts in change
    /// what every later call compares.
    /// </summary>
    [Fact]
    public void TheOptionDoesNotLeakIntoTheNextComparison()
    {
        var (subject, expectation) = DifferingOnlyInternally();

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers())));

        subject.Should().BeEquivalentTo(expectation);
    }

    /// <summary>
    /// The engine is driven by the expectation's members, so the option has to reach the subject
    /// side too — otherwise an internal member present on both would be looked up in a subject table
    /// that does not contain it and reported as missing.
    /// </summary>
    [Fact]
    public void TheSubjectSideSeesTheInternalMemberToo()
    {
        var subject = new InternalMemberPoco { Name = "a", Revision = 3 };
        var expectation = new InternalMemberPoco { Name = "a", Revision = 3 };

        subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers());

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers()));
        Assert.Null(ex);
    }
}
