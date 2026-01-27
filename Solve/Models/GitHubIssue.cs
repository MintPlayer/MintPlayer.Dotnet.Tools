namespace Solve.Models;

/// <summary>
/// Represents a GitHub issue with its details.
/// </summary>
public class GitHubIssue
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets the issue type based on labels.
    /// </summary>
    public string GetIssueType()
    {
        var labelSet = new HashSet<string>(Labels.Select(l => l.ToLowerInvariant()));

        if (labelSet.Contains("bug") || labelSet.Contains("bugfix"))
            return "Bug Fix";
        if (labelSet.Contains("enhancement") || labelSet.Contains("feature"))
            return "Feature";
        if (labelSet.Contains("documentation") || labelSet.Contains("docs"))
            return "Documentation";
        if (labelSet.Contains("refactor") || labelSet.Contains("refactoring"))
            return "Refactor";
        if (labelSet.Contains("test") || labelSet.Contains("testing"))
            return "Test";

        return "Feature"; // Default
    }

    /// <summary>
    /// Gets the priority based on labels.
    /// </summary>
    public string GetPriority()
    {
        var labelSet = new HashSet<string>(Labels.Select(l => l.ToLowerInvariant()));

        if (labelSet.Any(l => l.Contains("critical") || l.Contains("urgent") || l.Contains("p0")))
            return "High";
        if (labelSet.Any(l => l.Contains("high") || l.Contains("p1")))
            return "High";
        if (labelSet.Any(l => l.Contains("low") || l.Contains("p3")))
            return "Low";

        return "Medium"; // Default
    }

    /// <summary>
    /// Generates a URL-safe slug from the title.
    /// </summary>
    public string GetSlug(int maxLength = 30)
    {
        if (string.IsNullOrEmpty(Title))
            return "issue";

        // Convert to lowercase and replace spaces/special chars with hyphens
        var slug = Title.ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9]+", "-");
        slug = slug.Trim('-');

        // Truncate if necessary
        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength].TrimEnd('-');
        }

        return string.IsNullOrEmpty(slug) ? "issue" : slug;
    }
}
