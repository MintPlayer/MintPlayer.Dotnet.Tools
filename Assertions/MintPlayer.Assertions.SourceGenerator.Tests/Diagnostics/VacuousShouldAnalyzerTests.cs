using MintPlayer.Assertions.SourceGenerator.Tests._Infrastructure;

namespace MintPlayer.Assertions.SourceGenerator.Tests.Diagnostics;

/// <summary>
/// MPA0002 — <c>Should()</c> with no assertion chained onto it. A test that calls it and asserts
/// nothing passes silently, which is the failure this analyzer exists to prevent.
/// </summary>
public class VacuousShouldAnalyzerTests
{
    private const string Analyzer = "VacuousShouldAnalyzer";

    [Fact]
    public async Task ItFlagsAShouldWithNoAssertion()
    {
        var diagnostics = await AnalyzerHarness.RunAnalyzerAsync(Analyzer, """
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    var value = 1;
                    value.Should();
                }
            }
            """);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("MPA0002");
    }

    [Fact]
    public async Task ItIgnoresAShouldThatIsAssertedOn()
    {
        var diagnostics = await AnalyzerHarness.RunAnalyzerAsync(Analyzer, """
            using MintPlayer.Assertions;

            public class Test
            {
                public void Run()
                {
                    var value = 1;
                    value.Should().Be(1);
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ItAdvertisesAWellFormedDescriptor()
    {
        var descriptors = AnalyzerHarness.DescriptorsOf(Analyzer);

        descriptors.Should().ContainSingle();
        descriptors[0].Id.Should().Be("MPA0002");
        descriptors[0].IsEnabledByDefault.Should().BeTrue();
        descriptors[0].Title.ToString().Should().NotBeEmpty();
        descriptors[0].MessageFormat.ToString().Should().NotBeEmpty();
    }
}
