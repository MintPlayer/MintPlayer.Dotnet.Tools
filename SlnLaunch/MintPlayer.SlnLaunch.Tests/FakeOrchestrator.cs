using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

internal sealed class FakeOrchestrator : IProcessOrchestrator
{
    public LaunchPlan? Plan { get; private set; }
    public LaunchRunOptions? Options { get; private set; }
    public bool WasCalled { get; private set; }
    public int Result { get; set; }

    public Task<int> RunAsync(LaunchPlan plan, LaunchRunOptions options, CancellationToken cancellationToken)
    {
        WasCalled = true;
        Plan = plan;
        Options = options;
        return Task.FromResult(Result);
    }
}
