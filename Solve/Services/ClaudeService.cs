using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using Solve.Models;

namespace Solve.Services;

[Register(typeof(IClaudeService), ServiceLifetime.Scoped, "SolveServices")]
public class ClaudeService : IClaudeService
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LaunchForIssueAsync(GitHubIssue issue, string? planPath, string? prdPath, CancellationToken cancellationToken = default)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"You are working on GitHub issue #{issue.Number}.");
        prompt.AppendLine();
        prompt.AppendLine($"## Issue: {issue.Title}");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(issue.Body))
        {
            prompt.AppendLine("### Description");
            prompt.AppendLine(issue.Body);
            prompt.AppendLine();
        }

        if (issue.Labels.Count > 0)
        {
            prompt.AppendLine($"### Labels: {string.Join(", ", issue.Labels)}");
            prompt.AppendLine();
        }

        if (!string.IsNullOrEmpty(prdPath) && File.Exists(prdPath))
        {
            prompt.AppendLine($"### PRD: {prdPath}");
        }

        if (!string.IsNullOrEmpty(planPath) && File.Exists(planPath))
        {
            prompt.AppendLine($"### Development Plan: {planPath}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Please read the PRD and development plan, then analyze the requirements and begin implementation.");

        return await LaunchWithPromptAsync(prompt.ToString(), cancellationToken);
    }

    public async Task<bool> LaunchWithPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            // Write prompt to temp file to avoid shell escaping issues
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, prompt, cancellationToken);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "claude",
                        Arguments = $"--print < \"{tempFile}\"",
                        UseShellExecute = true,
                        CreateNoWindow = false
                    }
                };

                process.Start();

                // Don't wait for Claude to exit - it runs interactively
                return true;
            }
            finally
            {
                // Clean up temp file after a delay (Claude should have read it by then)
                _ = Task.Delay(5000, cancellationToken).ContinueWith(_ =>
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }, TaskScheduler.Default);
            }
        }
        catch
        {
            return false;
        }
    }
}
