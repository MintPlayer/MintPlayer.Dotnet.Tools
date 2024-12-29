using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Commands.Read;

[CommandOption<Options.FileOption>(Required = true)]
public class ReadSizeCommand : ICommand<ReadSizeCommandInput>
{
    public string Name => "size";

    public string Description => "Get file size";
}

public partial class ReadSizeCommandInput { }