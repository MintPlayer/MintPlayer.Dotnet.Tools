using System.Text.RegularExpressions;

namespace MintPlayer.Solve.Models;

/// <summary>
/// Represents a parsed GitHub issue reference.
/// </summary>
public partial class IssueReference
{
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public int Number { get; set; }

    /// <summary>
    /// Parses an issue reference from various formats.
    /// </summary>
    /// <param name="input">The input string (URL, short ref, or number)</param>
    /// <param name="currentRepoOwner">Owner from current git repo context</param>
    /// <param name="currentRepoName">Repo name from current git repo context</param>
    /// <returns>Parsed issue reference or null if parsing fails</returns>
    public static IssueReference? Parse(string? input, string? currentRepoOwner = null, string? currentRepoName = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        // Full URL: https://github.com/owner/repo/issues/123
        var urlMatch = GitHubUrlRegex().Match(input);
        if (urlMatch.Success)
        {
            return new IssueReference
            {
                Owner = urlMatch.Groups["owner"].Value,
                Repo = urlMatch.Groups["repo"].Value,
                Number = int.Parse(urlMatch.Groups["number"].Value)
            };
        }

        // Short reference: owner/repo#123
        var shortMatch = ShortRefRegex().Match(input);
        if (shortMatch.Success)
        {
            return new IssueReference
            {
                Owner = shortMatch.Groups["owner"].Value,
                Repo = shortMatch.Groups["repo"].Value,
                Number = int.Parse(shortMatch.Groups["number"].Value)
            };
        }

        // Number only: #123 or 123
        var numberMatch = NumberOnlyRegex().Match(input);
        if (numberMatch.Success)
        {
            return new IssueReference
            {
                Owner = currentRepoOwner,
                Repo = currentRepoName,
                Number = int.Parse(numberMatch.Groups["number"].Value)
            };
        }

        return null;
    }

    /// <summary>
    /// Extracts issue number from a branch name.
    /// Supports: issues/123, issues/#123, issues/123-description, feature/issue-123-description
    /// </summary>
    public static int? ExtractFromBranchName(string branchName)
    {
        // Pattern: issues/123 or issues/#123 or issues/123-description
        var issuesBranchMatch = IssuesBranchRegex().Match(branchName);
        if (issuesBranchMatch.Success)
        {
            return int.Parse(issuesBranchMatch.Groups["number"].Value);
        }

        // Pattern: feature/issue-123-description or similar
        var featureBranchMatch = FeatureIssueBranchRegex().Match(branchName);
        if (featureBranchMatch.Success)
        {
            return int.Parse(featureBranchMatch.Groups["number"].Value);
        }

        return null;
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo))
            return $"{Owner}/{Repo}#{Number}";
        return $"#{Number}";
    }

    [GeneratedRegex(@"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/issues/(?<number>\d+)")]
    private static partial Regex GitHubUrlRegex();

    [GeneratedRegex(@"^(?<owner>[^/]+)/(?<repo>[^#]+)#(?<number>\d+)$")]
    private static partial Regex ShortRefRegex();

    [GeneratedRegex(@"^#?(?<number>\d+)$")]
    private static partial Regex NumberOnlyRegex();

    [GeneratedRegex(@"^issues/[#]?(?<number>\d+)")]
    private static partial Regex IssuesBranchRegex();

    [GeneratedRegex(@"issue[s]?[-/](?<number>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureIssueBranchRegex();
}
