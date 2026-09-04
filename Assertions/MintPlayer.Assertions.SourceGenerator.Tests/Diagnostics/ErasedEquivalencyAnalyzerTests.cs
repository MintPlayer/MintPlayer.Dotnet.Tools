namespace MintPlayer.Assertions.SourceGenerator.Tests.Diagnostics;

/// <summary>
/// MPA0004 — an equivalency call with both sides cast to <c>object</c>. The result stays correct,
/// so this is a lost-guarantee hint: no generated accessors (reflection fallback instead) and no
/// usable options lambda.
/// </summary>
public class ErasedEquivalencyAnalyzerTests
{
    private const string Analyzer = "ErasedEquivalencyAnalyzer";

    private const string Poco = """
        public class Poco
        {
            public int Value { get; set; }
        }
        """;

    [Fact]
    public async Task ItFlagsACallWithBothSidesErasedToObject()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, $$"""
            using MintPlayer.Assertions;

            {{Poco}}

            public class Test
            {
                public void Run()
                {
                    var actual = new Poco();
                    var expected = new Poco();
                    ((object)actual).Should().BeEquivalentTo((object)expected);
                }
            }
            """);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("MPA0004");
    }

    [Fact]
    public async Task ItFlagsNotBeEquivalentToWithBothSidesErasedToObject()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, $$"""
            using MintPlayer.Assertions;

            {{Poco}}

            public class Test
            {
                public void Run()
                {
                    var actual = new Poco();
                    var expected = new Poco { Value = 1 };
                    ((object)actual).Should().NotBeEquivalentTo((object)expected);
                }
            }
            """);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("MPA0004");
    }

    [Fact]
    public async Task ItIgnoresASubjectOnlyCastWithATypedExpectation()
    {
        // The form the repo's own tests and benchmarks use: the typed expectation still registers
        // generated accessors, so nothing is lost.
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, $$"""
            using MintPlayer.Assertions;

            {{Poco}}

            public class Test
            {
                public void Run()
                {
                    var actual = new Poco();
                    var expected = new Poco();
                    ((object)actual).Should().BeEquivalentTo(expected);
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresAFullyTypedCall()
    {
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, $$"""
            using MintPlayer.Assertions;

            {{Poco}}

            public class Test
            {
                public void Run()
                {
                    var actual = new Poco();
                    var expected = new Poco();
                    actual.Should().BeEquivalentTo(expected);
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresStringEquivalency()
    {
        // StringAssertions.BeEquivalentTo is a case-insensitive string compare, an entirely
        // different method — flagging it would be a pure false positive.
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, """
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    "abc".Should().BeEquivalentTo("ABC");
                    "abc".Should().NotBeEquivalentTo("def");
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresAnExpectationThatIsMerelyTypedObject()
    {
        // TExpectation infers to object here, from real typed values, with no cast written
        // anywhere. Deliberately NOT flagged: variables and parameters that happen to be typed
        // object are the legitimate case (generic helpers, extension authors forwarding a
        // subject), so the analyzer keys on the explicit (object) cast instead of the inferred
        // type argument. That trades a few missed erasures for zero noise.
        var diagnostics = await Harness.Instance.RunAnalyzerAsync(Analyzer, $$"""
            using MintPlayer.Assertions;

            {{Poco}}

            public class Test
            {
                public void Run()
                {
                    object actual = new Poco();
                    object expected = new Poco();
                    actual.Should().BeEquivalentTo(expected);
                }

                public void Forward<T>(T actual, T expected)
                {
                    actual.Should().BeEquivalentTo(expected);
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ItAdvertisesAWellFormedDescriptor()
    {
        var descriptors = Harness.Instance.DescriptorsOf(Analyzer);

        descriptors.Should().ContainSingle();
        descriptors[0].Id.Should().Be("MPA0004");
        descriptors[0].DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
        descriptors[0].IsEnabledByDefault.Should().BeTrue();
        descriptors[0].Title.ToString().Should().NotBeEmpty();
        descriptors[0].MessageFormat.ToString().Should().NotBeEmpty();
    }
}
