using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Commands;

[CommandOption<Options.FileOption>]
[CommandOption<Options.DelayOption>]
[SubCommand<Read.ReadSizeCommand>]
internal class ReadCommand : ICommand
{
    public string Name => "read";

    public string Description => "Read a file";
}
