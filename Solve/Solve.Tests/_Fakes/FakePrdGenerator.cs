using Solve.Models;
using Solve.Services;

namespace Solve.Tests._Fakes;

/// <summary>
/// An in-memory PRD/plan store — no files touched.
/// </summary>
/// <remarks>
/// <see cref="SaveThrows"/> models the real generator refusing to overwrite an existing document
/// without <c>force</c>. It throws <see cref="InvalidOperationException"/> because that is what the
/// commands catch and turn into a warning; a different exception type would escape and fail the
/// command, which is exactly the distinction worth testing.
/// </remarks>
internal sealed class FakePrdGenerator : IPrdGenerator
{
    public string PlanContent { get; set; } = "# Plan";
    public string PrdContent { get; set; } = "# PRD";
    public bool PlanOnDisk { get; set; }
    public bool PrdOnDisk { get; set; }
    public WorkStatus? Status { get; set; }

    /// <summary>When set, both save operations throw it.</summary>
    public InvalidOperationException? SaveThrows { get; set; }

    public string? SavedPlan { get; private set; }
    public string? SavedPrd { get; private set; }
    public bool SavedPlanWithForce { get; private set; }
    public bool SavedPrdWithForce { get; private set; }

    public Task<string> GeneratePlanAsync(GitHubIssue issue, CancellationToken cancellationToken = default)
        => Task.FromResult(PlanContent);

    public Task<string> GeneratePrdAsync(GitHubIssue issue, CancellationToken cancellationToken = default)
        => Task.FromResult(PrdContent);

    public Task<string> SavePlanAsync(int issueNumber, string content, bool force = false, CancellationToken cancellationToken = default)
    {
        if (SaveThrows is not null) throw SaveThrows;
        SavedPlan = content;
        SavedPlanWithForce = force;
        return Task.FromResult(GetPlanPath(issueNumber));
    }

    public Task<string> SavePrdAsync(int issueNumber, string content, bool force = false, CancellationToken cancellationToken = default)
    {
        if (SaveThrows is not null) throw SaveThrows;
        SavedPrd = content;
        SavedPrdWithForce = force;
        return Task.FromResult(GetPrdPath(issueNumber));
    }

    public bool PlanExists(int issueNumber) => PlanOnDisk;

    public bool PrdExists(int issueNumber) => PrdOnDisk;

    public string GetPlanPath(int issueNumber) => $"docs/plan-{issueNumber}.md";

    public string GetPrdPath(int issueNumber) => $"docs/prd-{issueNumber}.md";

    public Task<WorkStatus?> ParseWorkStatusAsync(int issueNumber, string issueTitle, CancellationToken cancellationToken = default)
        => Task.FromResult(Status);
}
