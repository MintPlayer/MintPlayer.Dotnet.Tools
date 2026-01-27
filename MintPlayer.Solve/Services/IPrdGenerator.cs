using MintPlayer.Solve.Models;

namespace MintPlayer.Solve.Services;

/// <summary>
/// Service for generating PRD and development plan documents.
/// </summary>
public interface IPrdGenerator
{
    /// <summary>
    /// Generates a development plan document.
    /// </summary>
    Task<string> GeneratePlanAsync(GitHubIssue issue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a PRD document.
    /// </summary>
    Task<string> GeneratePrdAsync(GitHubIssue issue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the development plan to the standard location.
    /// </summary>
    Task<string> SavePlanAsync(int issueNumber, string content, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the PRD to the standard location.
    /// </summary>
    Task<string> SavePrdAsync(int issueNumber, string content, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a plan exists for the issue.
    /// </summary>
    bool PlanExists(int issueNumber);

    /// <summary>
    /// Checks if a PRD exists for the issue.
    /// </summary>
    bool PrdExists(int issueNumber);

    /// <summary>
    /// Gets the plan file path for an issue.
    /// </summary>
    string GetPlanPath(int issueNumber);

    /// <summary>
    /// Gets the PRD file path for an issue.
    /// </summary>
    string GetPrdPath(int issueNumber);

    /// <summary>
    /// Reads and parses the PRD to get work status.
    /// </summary>
    Task<WorkStatus?> ParseWorkStatusAsync(int issueNumber, string issueTitle, CancellationToken cancellationToken = default);
}
