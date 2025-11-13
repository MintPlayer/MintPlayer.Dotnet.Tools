using MintPlayer.CliGenerator.Attributes;

namespace MintPlayer.Verz;

[CliRootCommand(Name = "verz", Description = "MintPlayer.Verz: compute package versions across feeds")]
public partial class VerzCommand : ICliCommand
{
    public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
}
