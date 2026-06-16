using MintPlayer.SlnLaunch.Models;

namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Turns a parsed launch profile into a ready-to-run <see cref="LaunchPlan"/> — resolving project
/// paths, filtering out non-launched entries, and mapping each <c>DebugTarget</c> to a
/// <c>dotnet run</c>/<c>dotnet watch</c> invocation.
/// </summary>
public interface ILaunchPlanBuilder
{
    /// <param name="profile">The selected profile.</param>
    /// <param name="baseDirectory">Directory that project paths are relative to (the .slnLaunch directory).</param>
    /// <param name="options">Build options and the forwardable-argument pool.</param>
    /// <exception cref="SlnLaunchException">A referenced project file does not exist.</exception>
    LaunchPlan Build(LaunchProfile profile, string baseDirectory, LaunchPlanOptions options);
}
