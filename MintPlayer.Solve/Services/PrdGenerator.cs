using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Solve.Models;

namespace MintPlayer.Solve.Services;

[Register(typeof(IPrdGenerator), ServiceLifetime.Scoped, "SolveServices")]
public partial class PrdGenerator : IPrdGenerator
{
    private const string PlansDirectory = ".claude/plans";
    private const string DocsDirectory = "docs";

    public Task<string> GeneratePlanAsync(GitHubIssue issue, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Development Plan: Issue #{issue.Number}");
        sb.AppendLine();
        sb.AppendLine($"**Issue**: #{issue.Number}");
        sb.AppendLine($"**Title**: {issue.Title}");
        sb.AppendLine($"**Type**: {issue.GetIssueType()}");
        sb.AppendLine($"**Priority**: {issue.GetPriority()}");
        sb.AppendLine();
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine(ExtractSummary(issue.Body));
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Problem Statement");
        sb.AppendLine();
        sb.AppendLine("### Current Behavior");
        sb.AppendLine("<What currently happens>");
        sb.AppendLine();
        sb.AppendLine("### Expected Behavior");
        sb.AppendLine("<What should happen after implementation>");
        sb.AppendLine();
        sb.AppendLine("### Impact");
        sb.AppendLine("<Business/user impact>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Technical Analysis");
        sb.AppendLine();
        sb.AppendLine("### Files to Modify");
        sb.AppendLine("<List of files that need changes>");
        sb.AppendLine();
        sb.AppendLine("### Dependencies");
        sb.AppendLine("<External services, packages, or other issues this depends on>");
        sb.AppendLine();
        sb.AppendLine("### Architecture Considerations");
        sb.AppendLine("<Any architectural decisions or patterns to follow>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Implementation Plan");
        sb.AppendLine();
        sb.AppendLine("### Phase 1: Initial Implementation");
        sb.AppendLine("1. <Step 1>");
        sb.AppendLine("2. <Step 2>");
        sb.AppendLine();
        sb.AppendLine("### Phase 2: Testing & Refinement");
        sb.AppendLine("1. <Step 1>");
        sb.AppendLine("2. <Step 2>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Test Scenarios");
        sb.AppendLine();
        sb.AppendLine("### Scenario 1: Happy Path");
        sb.AppendLine("- **Given**: <preconditions>");
        sb.AppendLine("- **When**: <action>");
        sb.AppendLine("- **Then**: <expected result>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Acceptance Criteria");
        sb.AppendLine();
        ExtractAcceptanceCriteria(issue.Body, sb);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Related Files");
        sb.AppendLine();
        sb.AppendLine("- <file1>");
        sb.AppendLine("- <file2>");

        return Task.FromResult(sb.ToString());
    }

    public Task<string> GeneratePrdAsync(GitHubIssue issue, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now.ToString("yyyy-MM-dd");

        sb.AppendLine($"# Product Requirements Document: {issue.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Issue**: #{issue.Number}");
        sb.AppendLine($"**Title**: {issue.Title}");
        sb.AppendLine("**Status**: Draft");
        sb.AppendLine($"**Created**: {now}");
        sb.AppendLine($"**Last Updated**: {now}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine(ExtractSummary(issue.Body));
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Goals & Objectives");
        sb.AppendLine();
        sb.AppendLine("### Primary Goals");
        sb.AppendLine("- <Goal 1>");
        sb.AppendLine("- <Goal 2>");
        sb.AppendLine();
        sb.AppendLine("### Success Metrics");
        sb.AppendLine("- <Metric 1>");
        sb.AppendLine("- <Metric 2>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Functional Requirements");
        sb.AppendLine();
        sb.AppendLine("### Must Have (P0)");
        sb.AppendLine("- [ ] **FR-1**: <requirement>");
        sb.AppendLine("- [ ] **FR-2**: <requirement>");
        sb.AppendLine();
        sb.AppendLine("### Should Have (P1)");
        sb.AppendLine("- [ ] **FR-3**: <requirement>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Timeline & Milestones");
        sb.AppendLine();
        sb.AppendLine("### Milestone 1: Initial Implementation");
        sb.AppendLine("- [ ] Task 1");
        sb.AppendLine("- [ ] Task 2");
        sb.AppendLine();
        sb.AppendLine("### Milestone 2: Testing & Refinement");
        sb.AppendLine("- [ ] Task 3");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Open Questions");
        sb.AppendLine();
        sb.AppendLine("- [ ] <Question 1>");
        sb.AppendLine("- [ ] <Question 2>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Technical Notes (Issue-Specific)");
        sb.AppendLine();
        sb.AppendLine("<Only include notes specific to THIS issue>");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Related");
        sb.AppendLine($"- Issue #{issue.Number}");

        return Task.FromResult(sb.ToString());
    }

    public async Task<string> SavePlanAsync(int issueNumber, string content, bool force = false, CancellationToken cancellationToken = default)
    {
        var path = GetPlanPath(issueNumber);

        if (File.Exists(path) && !force)
            throw new InvalidOperationException($"Plan already exists at {path}. Use --force to overwrite.");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }

    public async Task<string> SavePrdAsync(int issueNumber, string content, bool force = false, CancellationToken cancellationToken = default)
    {
        var path = GetPrdPath(issueNumber);

        if (File.Exists(path) && !force)
            throw new InvalidOperationException($"PRD already exists at {path}. Use --force to overwrite.");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }

    public bool PlanExists(int issueNumber) => File.Exists(GetPlanPath(issueNumber));

    public bool PrdExists(int issueNumber) => File.Exists(GetPrdPath(issueNumber));

    public string GetPlanPath(int issueNumber) => Path.Combine(PlansDirectory, $"issue_{issueNumber}.md");

    public string GetPrdPath(int issueNumber) => Path.Combine(DocsDirectory, $"issue_{issueNumber}_PRD.md");

    public async Task<WorkStatus?> ParseWorkStatusAsync(int issueNumber, string issueTitle, CancellationToken cancellationToken = default)
    {
        var prdPath = GetPrdPath(issueNumber);
        if (!File.Exists(prdPath))
            return null;

        var content = await File.ReadAllTextAsync(prdPath, cancellationToken);
        var status = new WorkStatus
        {
            IssueNumber = issueNumber,
            IssueTitle = issueTitle
        };

        // Extract PRD status
        var statusMatch = PrdStatusRegex().Match(content);
        if (statusMatch.Success)
            status.PrdStatus = statusMatch.Groups[1].Value.Trim();

        // Count requirements
        var checkedMatches = CheckedItemRegex().Matches(content);
        var uncheckedMatches = UncheckedItemRegex().Matches(content);

        status.CompletedRequirements = checkedMatches.Count;
        status.TotalRequirements = checkedMatches.Count + uncheckedMatches.Count;

        // Extract completed items
        foreach (Match match in checkedMatches)
        {
            status.CompletedItems.Add(match.Groups[1].Value.Trim());
        }

        // Extract remaining items
        foreach (Match match in uncheckedMatches)
        {
            status.RemainingItems.Add(match.Groups[1].Value.Trim());
        }

        // Extract open questions (unchecked items in Open Questions section)
        var questionsSection = OpenQuestionsSectionRegex().Match(content);
        if (questionsSection.Success)
        {
            var questionMatches = UncheckedItemRegex().Matches(questionsSection.Groups[1].Value);
            foreach (Match match in questionMatches)
            {
                status.OpenQuestions.Add(match.Groups[1].Value.Trim());
            }
        }

        // Determine implementation status
        status.ImplementationStatus = status.CompletionPercentage switch
        {
            0 => "Not Started",
            100 => "Complete",
            _ => $"{status.CompletionPercentage}% Complete"
        };

        return status;
    }

    private static string ExtractSummary(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "<Brief description of what needs to be done and why>";

        // Take first paragraph or first 200 characters
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || trimmed.StartsWith('-') || trimmed.StartsWith('*'))
                continue;

            summary.AppendLine(trimmed);
            if (summary.Length > 200)
                break;
        }

        var result = summary.ToString().Trim();
        return string.IsNullOrEmpty(result)
            ? "<Brief description of what needs to be done and why>"
            : result;
    }

    private static void ExtractAcceptanceCriteria(string body, StringBuilder sb)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine("- [ ] <Criterion 1>");
            sb.AppendLine("- [ ] <Criterion 2>");
            return;
        }

        // Look for existing checkboxes in the body
        var checkboxMatches = CheckboxRegex().Matches(body);
        if (checkboxMatches.Count > 0)
        {
            foreach (Match match in checkboxMatches)
            {
                sb.AppendLine($"- [ ] {match.Groups[1].Value.Trim()}");
            }
        }
        else
        {
            sb.AppendLine("- [ ] <Criterion 1>");
            sb.AppendLine("- [ ] <Criterion 2>");
        }
    }

    [GeneratedRegex(@"\*\*Status\*\*:\s*(.+)")]
    private static partial Regex PrdStatusRegex();

    [GeneratedRegex(@"- \[x\] (.+)")]
    private static partial Regex CheckedItemRegex();

    [GeneratedRegex(@"- \[ \] (.+)")]
    private static partial Regex UncheckedItemRegex();

    [GeneratedRegex(@"## Open Questions\s+([\s\S]*?)(?=\n##|$)")]
    private static partial Regex OpenQuestionsSectionRegex();

    [GeneratedRegex(@"- \[[ x]\] (.+)")]
    private static partial Regex CheckboxRegex();
}
