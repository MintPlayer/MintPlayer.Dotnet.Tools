namespace Solve.Models;

/// <summary>
/// Represents the current work status for an issue.
/// </summary>
public class WorkStatus
{
    public int IssueNumber { get; set; }
    public string IssueTitle { get; set; } = string.Empty;
    public string PrdStatus { get; set; } = "Not Started";
    public string ImplementationStatus { get; set; } = "Not Started";

    public int TotalRequirements { get; set; }
    public int CompletedRequirements { get; set; }

    public List<string> CompletedItems { get; set; } = [];
    public List<string> RemainingItems { get; set; } = [];
    public List<string> OpenQuestions { get; set; } = [];
    public List<string> Blockers { get; set; } = [];

    public bool HasUncommittedChanges { get; set; }
    public string? OpenPrUrl { get; set; }

    public int CompletionPercentage =>
        TotalRequirements > 0 ? (CompletedRequirements * 100) / TotalRequirements : 0;
}
