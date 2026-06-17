using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

internal sealed class FakeOrchestrator : IProcessOrchestrator
{
    public LaunchPlan? Plan { get; private set; }
    public LaunchRunOptions? Options { get; private set; }
    public bool WasCalled { get; private set; }
    public int Result { get; set; }

    public bool BuildWasCalled { get; private set; }
    public LaunchBuildOptions? BuildOptions { get; private set; }
    public bool BuildResult { get; set; } = true;

    /// <summary>True when <see cref="BuildAsync"/> had already run by the time <see cref="RunAsync"/> was called.</summary>
    public bool BuildRanBeforeRun { get; private set; }

    public Task<int> RunAsync(LaunchPlan plan, LaunchRunOptions options, CancellationToken cancellationToken)
    {
        BuildRanBeforeRun = BuildWasCalled;
        WasCalled = true;
        Plan = plan;
        Options = options;
        return Task.FromResult(Result);
    }

    public Task<bool> BuildAsync(LaunchPlan plan, LaunchBuildOptions options, CancellationToken cancellationToken)
    {
        BuildWasCalled = true;
        BuildOptions = options;
        Plan = plan;
        return Task.FromResult(BuildResult);
    }
}
