using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Options;

internal class FileOption : ICommandOption
{
    public string Name => "file";

    public string Description => "File";
}
