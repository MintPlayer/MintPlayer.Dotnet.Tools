using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Options;

internal class DelayOption : ICommandOption
{
    public string Name => "delay";

    public string Description => "Delay";
}
