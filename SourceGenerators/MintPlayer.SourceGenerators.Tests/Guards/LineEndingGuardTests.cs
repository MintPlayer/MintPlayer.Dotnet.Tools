using System.Text.RegularExpressions;

namespace MintPlayer.SourceGenerators.Tests.Guards;

/// <summary>
/// Every generated file uses one line ending: <see cref="Environment.NewLine"/>, the one
/// <see cref="System.CodeDom.Compiler.IndentedTextWriter"/> writes.
/// </summary>
/// <remarks>
/// <para>
/// <c>Producer.Header</c> is a raw string literal, so its line breaks are those of the checkout the package was built
/// from. Written with <c>writer.WriteLine(Header)</c>, a package built on Linux and used on Windows emitted an LF header
/// above a CRLF body. Every generator now writes it with <c>Producer.WriteHeader</c>, one line at a time.
/// </para>
/// <para>
/// This guard runs every generator over its <see cref="FixedFileSetGuardTests"/> corpus. On a machine whose checkout
/// uses the same line ending as <see cref="Environment.NewLine"/> the old bug cannot show here, which is why
/// <c>IndentedTextWriterExtensionsTests</c> in the Tools tests feeds <c>WriteLines</c> and <c>WriteHeader</c> line
/// breaks spelled out as escapes, into a writer set to the other convention.
/// </para>
/// </remarks>
public class LineEndingGuardTests
{
    private static readonly Regex LineBreak = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    public static TheoryData<FixedFileSetGuardTests.Case> AllCases() => FixedFileSetGuardTests.AllCases();

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EveryGeneratedFile_UsesEnvironmentNewLine_Only(FixedFileSetGuardTests.Case c)
    {
        var run = FixedFileSetGuardTests.Run(c, 2);
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
