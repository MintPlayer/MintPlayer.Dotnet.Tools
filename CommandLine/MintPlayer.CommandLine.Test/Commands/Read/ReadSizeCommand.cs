using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Commands.Read;

public class ReadSizeCommand : ICommand
{
    public string Name => "size";

    public string Description => "Get file size";
}
