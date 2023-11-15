using System.Text.RegularExpressions;

namespace MintPlayer.StringExtensions;


// https://github.com/MintPlayer/MintPlayer.StringFormat
// https://github.com/MintPlayer/MintPlayer.StringOps
// https://github.com/MintPlayer/MintPlayer.Random => RandomString
public static class StringExtensions
{
    private static readonly Regex rgxPlaceholder = new Regex(@"\{(?<index>[0-9]+?)((?<dp1>\:)(?<format>[0-9A-Za-z]+?))?(?<anchor>(\:[A-Za-z\.]*)*)\}", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

    /// <summary>This method allows you to embed additional information in a <see cref="System.Text.RegularExpressions.Regex"/>'s placeholders.</summary>
    /// <param name="format">Format containing anchored placeholders.</param>
    /// <param name="anchors">Will be seeded with an array where each index contains an array of the corresponding anchors discovered in the <see cref="System.Text.RegularExpressions.Regex"/>.</param>
    /// <param name="args">Parameters to parse into the <see cref="System.Text.RegularExpressions.Regex"/>.</param>
    /// <returns>A formatted string.</returns>
    public static string FormatAdvanced(string format, out string[][] anchors, params object[] args)
    {
        var matches = rgxPlaceholder.Matches(format);
        var matchArray = new Match[matches.Count];
        matches.CopyTo(matchArray, 0);

        var result = matchArray
            .Select(m => new { Index = int.Parse(m.Groups["index"].Value), Format = m.Groups["format"].Value, Anchor = m.Groups["anchor"].Value.TrimStart(':') })
            .GroupBy(m => m.Index)
            .Select(m => new
            {
                Index = m.Key,
                Formats = m.Select(g => g.Format).Where(f => !string.IsNullOrEmpty(f)).ToList(),
                Anchors = m.Select(g => g.Anchor).Where(a => !string.IsNullOrEmpty(a)).ToList()
            })
            .ToList();

        var newFormatString = rgxPlaceholder.Replace(format, "{${index}${dp1}${format}}");
        anchors = Enumerable.Range(0, result.Count)
            .Select(i => result.SingleOrDefault(r => r.Index == i))
            .Select(r =>
            {
                if (r == null) return [];
                else return r.Anchors.ToArray();
            })
            .ToArray();


        return string.Format(newFormatString, args);
    }
}