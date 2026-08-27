namespace MintPlayer.EnumerableExtensions.Tests;

public class OneOfTests
{
    private enum Status { Removed, Unchanged, Added }

    [Fact]
    public void OneOf_WhenPresent_IsTrue()
        => Status.Removed.OneOf([Status.Removed, Status.Unchanged]).Should().BeTrue();

    [Fact]
    public void OneOf_WhenAbsent_IsFalse()
        => Status.Added.OneOf([Status.Removed, Status.Unchanged]).Should().BeFalse();

    [Fact]
    public void OneOf_OnEmptySet_IsFalse()
        => 1.OneOf([]).Should().BeFalse();

    [Fact]
    public void OneOf_UsesDefaultEquality_ForStrings()
    {
        "b".OneOf(["a", "b"]).Should().BeTrue();
        "B".OneOf(["a", "b"]).Should().BeFalse();
    }

    [Fact]
    public void OneOf_MatchesNull_WhenTheSetContainsNull()
    {
        string? value = null;
        value.OneOf([null, "a"]).Should().BeTrue();
    }

    [Fact]
    public void OneOf_OnNullSet_Throws()
    {
        var act = () => 1.OneOf(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
