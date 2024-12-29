using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Options;

internal class DelayOption : ICommandOption<int>
{
    public string Name => "delay";

    public string Description => "Delay";

    public int GetDefaultValue() => 1000;
}
