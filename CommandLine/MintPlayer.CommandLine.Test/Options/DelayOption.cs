using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Options;

/// <summary>
/// Asks the user for a delay timespan
/// </summary>
internal class DelayOption : ICommandOption<int>
{
    public string Name => "delay";

    public string Description => "Delay";

    public int GetDefaultValue() => 1000;
}
