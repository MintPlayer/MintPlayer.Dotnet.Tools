using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Options;

internal class FileOption : ICommandOption<string>
{
    public string Name => "file";

    public string Description => "File";

    public string GetDefaultValue() => throw new InvalidOperationException("The filename should always be passed");
}
