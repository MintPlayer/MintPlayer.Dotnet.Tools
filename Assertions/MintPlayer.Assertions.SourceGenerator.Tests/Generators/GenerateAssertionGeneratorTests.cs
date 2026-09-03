namespace MintPlayer.Assertions.SourceGenerator.Tests.Generators;

/// <summary>
/// <c>[GenerateAssertion]</c> turns a plain predicate into a fluent assertion:
/// <c>static bool IsEven(int)</c> becomes <c>value.Should().BeEven()</c>.
/// </summary>
public class GenerateAssertionGeneratorTests
{
    private const string Generator = "GenerateAssertionGenerator";

    private static string WithPredicate(string signature, string body = "=> true;") => $$"""
        using MintPlayer.Assertions;

        namespace Demo;

        public static class Predicates
        {
            [GenerateAssertion]
            public static {{signature}} {{body}}
        }
        """;

    [Fact]
    public void ItGeneratesAnAssertionForASimplePredicate()
    {
        var run = Harness.Instance.RunGenerator(Generator, WithPredicate("bool IsEven(int value)", "=> value % 2 == 0;"));

        run.GeneratedSources.Should().NotBeEmpty();
        run.AllSources.Should().Contain("BeEven");
    }

    /// <summary>
    /// The naming rules are the whole user-facing contract of this generator — an assertion named
    /// wrongly is an assertion nobody can find.
    /// </summary>
    [Theory]
    [InlineData("IsEven", "BeEven")]
    [InlineData("HasItems", "HaveItems")]
    [InlineData("CanFly", "BeAbleToFly")]
    [InlineData("Valid", "BeValid")]
    public void ItDerivesTheAssertionNameFromThePredicateName(string predicate, string expected)
    {
        var run = Harness.Instance.RunGenerator(Generator, WithPredicate($"bool {predicate}(int value)"));

        run.AllSources.Should().Contain(expected);
    }

    /// <summary>
    /// "Is" only counts as a prefix when a capitalised word follows it, so a predicate genuinely
    /// called <c>Isolated</c> must not become <c>Beolated</c>.
    /// </summary>
    [Fact]
    public void ItDoesNotTreatAWordStartingWithIsAsThePrefix()
    {
        var run = Harness.Instance.RunGenerator(Generator, WithPredicate("bool Isolated(int value)"));

        run.AllSources.Should().Contain("BeIsolated");
        run.AllSources.Should().NotContain("Beolated");
    }

    [Fact]
    public void ItEmitsCompilableCode()
    {
        var run = Harness.Instance.RunGenerator(Generator, WithPredicate("bool IsEven(int value)", "=> value % 2 == 0;"));

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    [Fact]
    public void ItGeneratesNothingWithoutTheAttribute()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            namespace Demo;

            public static class Predicates
            {
                public static bool IsEven(int value) => value % 2 == 0;
            }
            """);

        run.AllSources.Should().NotContain("BeEven");
    }

    #region MPAG001 — unsupported shapes

    /// <summary>
    /// Every rejection path, each with the reason the generator reports. These are the branches
    /// that were entirely uncovered before this project existed: nothing drove the generator down
    /// a diagnostic path, so a rule that never fired would have looked identical to one that did.
    /// </summary>
    [Theory]
    [InlineData("bool IsEven(int value)", "static", "public bool IsEven(int value) => true;")]
    [InlineData("returns bool", "return bool", "public static int IsEven(int value) => 1;")]
    [InlineData("first parameter", "first parameter", "public static bool IsEven() => true;")]
    [InlineData("generic", "generic", "public static bool IsEven<T>(T value) => true;")]
    [InlineData("by-ref", "by-ref", "public static bool IsEven(ref int value) => true;")]
    public void ItReportsUnsupportedShapes(string _, string expectedReasonFragment, string member)
    {
        var run = Harness.Instance.RunGenerator(Generator, $$"""
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Predicates
            {
                [GenerateAssertion]
                {{member}}
            }
            """);

        var diagnostics = run.Of("MPAG001");
        diagnostics.Should().NotBeEmpty();
        diagnostics[0].GetMessage().Should().Contain(expectedReasonFragment);
    }

    [Fact]
    public void ItReportsAGenericDeclaringType()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Predicates<T>
            {
                [GenerateAssertion]
                public static bool IsEven(int value) => true;
            }
            """);

        run.Of("MPAG001").Should().NotBeEmpty();
    }

    /// <summary>
    /// A rejected method must not also produce an assertion — the generator has to drop it from
    /// the emission list, not merely warn about it.
    /// </summary>
    [Fact]
    public void ARejectedMethodProducesNoAssertion()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Predicates
            {
                [GenerateAssertion]
                public bool IsEven(int value) => true;
            }
            """);

        run.Of("MPAG001").Should().NotBeEmpty();
        run.AllSources.Should().NotContain("BeEven");
    }

    /// <summary>
    /// A supported and an unsupported method in the same file: the warning must not suppress the
    /// assertion for the one that is fine.
    /// </summary>
    [Fact]
    public void OneBadMethodDoesNotSuppressTheOthers()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public static class Predicates
            {
                [GenerateAssertion]
                public static bool IsEven(int value) => true;

                [GenerateAssertion]
                public bool IsOdd(int value) => true;
            }
            """);

        run.Of("MPAG001").Should().NotBeEmpty();
        run.AllSources.Should().Contain("BeEven");
        run.AllSources.Should().NotContain("BeOdd");
    }

    #endregion
}
