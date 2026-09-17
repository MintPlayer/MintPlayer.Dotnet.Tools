using System.ComponentModel;

namespace MintPlayer.Assertions.Tests;

/// <summary>A scanned type with one member of each kind, differing independently.</summary>
[AssertEquivalency]
public class KindedPoco
{
    public int Property { get; set; }
    public int Field;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Hidden { get; set; }
}

/// <summary>
/// The first equivalency options answered from generator-emitted member flags rather than from
/// reflection: <c>ExcludingFields</c>, <c>ExcludingProperties</c>,
/// <c>IncludingNonBrowsableMembers</c>.
/// </summary>
public class MemberKindOptionsTests
{
    [Fact]
    public void BothKindsAreComparedByDefault()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(new KindedPoco { Property = 9, Field = 1 })));
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(new KindedPoco { Property = 1, Field = 9 })));
    }

    [Fact]
    public void ExcludingFieldsLeavesTheFieldOutOfTheComparison()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };
        var expectation = new KindedPoco { Property = 1, Field = 9 };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingFields());
    }

    [Fact]
    public void ExcludingFieldsStillComparesProperties()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };
        var expectation = new KindedPoco { Property = 9, Field = 1 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.ExcludingFields()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Property", ex.Message);
    }

    [Fact]
    public void ExcludingPropertiesLeavesPropertiesOutOfTheComparison()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };
        var expectation = new KindedPoco { Property = 9, Field = 1 };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingProperties());
    }

    [Fact]
    public void ExcludingPropertiesStillComparesFields()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };
        var expectation = new KindedPoco { Property = 1, Field = 9 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.ExcludingProperties()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Field", ex.Message);
    }

    /// <summary>
    /// Excluding both leaves nothing to compare, which would make the assertion pass unconditionally
    /// — the engine's existing vacuity check catches that and says so, rather than going green.
    /// </summary>
    [Fact]
    public void ExcludingBothKindsIsRejectedAsVacuous()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };
        var expectation = new KindedPoco { Property = 9, Field = 9 };

        var ex = Record.Exception(() =>
            subject.Should().BeEquivalentTo(expectation, o => o.ExcludingFields().ExcludingProperties()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex.Message);
    }

    [Fact]
    public void ANonBrowsableMemberIsNotComparedByDefault()
    {
        var subject = new KindedPoco { Property = 1, Field = 1, Hidden = 1 };
        var expectation = new KindedPoco { Property = 1, Field = 1, Hidden = 9 };

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void IncludingNonBrowsableMembersComparesIt()
    {
        var subject = new KindedPoco { Property = 1, Field = 1, Hidden = 1 };
        var expectation = new KindedPoco { Property = 1, Field = 1, Hidden = 9 };

        var ex = Record.Exception(() =>
            subject.Should().BeEquivalentTo(expectation, o => o.IncludingNonBrowsableMembers()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Hidden", ex.Message);
    }

    /// <summary>The two halves of a selection pull in opposite directions and must compose.</summary>
    [Fact]
    public void IncludingAndExcludingCompose()
    {
        var subject = new KindedPoco { Property = 1, Field = 1, Hidden = 1 };
        var expectation = new KindedPoco { Property = 1, Field = 9, Hidden = 9 };

        // Fields out, non-browsable in: Field is ignored, Hidden is compared and differs.
        var ex = Record.Exception(() =>
            subject.Should().BeEquivalentTo(expectation, o => o.ExcludingFields().IncludingNonBrowsableMembers()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("- Hidden:", ex.Message);
        // The rendered expectation naturally mentions every member name, so the assertion is on the
        // DIFFERENCE line rather than on the message as a whole.
        Assert.DoesNotContain("- Field:", ex.Message);
    }

    /// <summary>
    /// The options are per comparison. The member tables are cached per (type, selection), and a
    /// cache keyed only by type — an easy mistake — would let one call change what every later call
    /// compares.
    /// </summary>
    [Fact]
    public void AnOptionDoesNotLeakIntoTheNextComparison()
    {
        var subject = new KindedPoco { Property = 1, Field = 1 };
        var expectation = new KindedPoco { Property = 1, Field = 9 };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingFields());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }
}
