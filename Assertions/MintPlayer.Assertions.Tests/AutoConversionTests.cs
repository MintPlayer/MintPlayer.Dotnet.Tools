namespace MintPlayer.Assertions.Tests;

/// <summary><c>WithAutoConversion</c>, and the one-directional guarantee it rests on.</summary>
public class AutoConversionTests
{
    [Fact]
    public void AStringConvertsToTheExpectedNumber()
    {
        var subject = new { Value = (object)"1" };
        var expectation = new { Value = (object)1 };

        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));

        subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion());
    }

    [Fact]
    public void NumericRepresentationsConvert()
    {
        var subject = new { Value = (object)1 };
        var expectation = new { Value = (object)1.0m };

        subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion());
    }

    /// <summary>
    /// The guarantee that makes this safe to reach for: conversion is tried only AFTER ordinary
    /// equality has failed, so it can rescue a representation difference and can never manufacture a
    /// failure that would otherwise have passed.
    /// </summary>
    [Fact]
    public void ConversionCannotTurnAPassIntoAFailure()
    {
        var subject = new { Value = (object)1, Text = "same" };
        var expectation = new { Value = (object)1, Text = "same" };

        subject.Should().BeEquivalentTo(expectation);
        subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion());
    }

    [Fact]
    public void AValueThatDoesNotConvertIsSimplyNotEqual()
    {
        var subject = new { Value = (object)"not a number" };
        var expectation = new { Value = (object)1 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion()));

        // A FormatException from the conversion must not escape as an error; it is a failed
        // comparison, reported as one.
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void AConvertibleValueThatDiffersStillFails()
    {
        var subject = new { Value = (object)"2" };
        var expectation = new { Value = (object)1 };

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion())));
    }

    /// <summary>Structural values are never converted — only IConvertible ones.</summary>
    [Fact]
    public void ConversionDoesNotReachStructuralValues()
    {
        var subject = new { Value = (object)new OptionNested { Label = "x" } };
        var expectation = new { Value = (object)"x" };

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion())));
    }

    [Fact]
    public void WithoutAutoConversionIsTheDefaultAndCanBeStatedOutLoud()
    {
        var subject = new { Value = (object)"1" };
        var expectation = new { Value = (object)1 };

        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion().WithoutAutoConversion())));
    }

    /// <summary>Culture must not decide whether a test passes.</summary>
    [Fact]
    public void ConversionIsCultureInvariant()
    {
        var subject = new { Value = (object)"1.5" };
        var expectation = new { Value = (object)1.5d };

        subject.Should().BeEquivalentTo(expectation, o => o.WithAutoConversion());
    }
}
