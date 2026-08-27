using Microsoft.Build.Framework;

namespace MintPlayer.Testing.MSBuild;

/// <summary>
/// A minimal <see cref="IBuildEngine"/> so an MSBuild task can be instantiated and driven
/// directly, with no MSBuild installation involved — <c>Microsoft.Build.Utilities.Core</c> is
/// just a NuGet package. Records everything the task logs so tests can assert on the
/// diagnostics rather than only on the return value.
///
/// Linked (not copied) into each MSBuild-task test project from eng/testing, because all
/// three tasks need exactly this and nothing more.
/// </summary>
internal sealed class FakeBuildEngine : IBuildEngine
{
    public List<BuildErrorEventArgs> Errors { get; } = [];
    public List<BuildWarningEventArgs> Warnings { get; } = [];
    public List<BuildMessageEventArgs> Messages { get; } = [];

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "test.proj";

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
    public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
    public void LogCustomEvent(CustomBuildEventArgs e) { }

    public bool BuildProjectFile(string projectFileName, string[] targetNames,
        System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
        => throw new NotSupportedException("These tests drive a single task, never a nested build.");

    /// <summary>All logged error messages, joined — handy for a single WithMessage assertion.</summary>
    public string ErrorText => string.Join(Environment.NewLine, Errors.Select(e => e.Message));

    public string WarningText => string.Join(Environment.NewLine, Warnings.Select(w => w.Message));
}
