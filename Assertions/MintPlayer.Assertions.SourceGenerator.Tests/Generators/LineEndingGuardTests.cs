using System.Text.RegularExpressions;

namespace MintPlayer.Assertions.SourceGenerator.Tests.Generators;

/// <summary>
/// Every generated file uses one line ending: <see cref="Environment.NewLine"/>.
/// </summary>
/// <remarks>
/// The counterpart of <c>MintPlayer.SourceGenerators.Tests.Guards.LineEndingGuardTests</c>. Both generators here write
/// <c>Producer.Header</c>, a raw string literal whose line breaks are those of the checkout the package was built from;
/// <c>writer.WriteLine(Header)</c> gave a package built on Linux an LF header above a CRLF body on Windows. <c>Header</c>
/// now normalizes its line breaks to <see cref="Environment.NewLine"/>.
/// </remarks>
public class LineEndingGuardTests
{
    private static readonly Regex LineBreak = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    public static TheoryData<FixedFileSetGuardTests.Case> AllCases() => FixedFileSetGuardTests.AllCases();

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EveryGeneratedFile_UsesEnvironmentNewLine_Only(FixedFileSetGuardTests.Case c)
    {
        var run = Harness.Instance.RunGenerator(c.Generator, c.Corpus(2));
        run.GeneratedSources.Should().NotBeEmpty($"the {c.Generator} corpus must make the generator emit something");

        foreach (var source in run.GeneratedSources)
        {
            var breaks = LineBreak.Matches(source.Source).Cast<Match>().Select(m => m.Value).ToList();
            breaks.Should().NotBeEmpty($"{source.HintName} has more than one line");
            breaks.Where(b => b != Environment.NewLine).Should().BeEmpty(
                $"every line break in {source.HintName} must be Environment.NewLine ({Escape(Environment.NewLine)}); "
                + $"found {string.Join(", ", breaks.Distinct().Select(Escape))}");
        }
    }

    private static string Escape(string lineBreak) => lineBreak.Replace("\r", "\\r").Replace("\n", "\\n");
}
