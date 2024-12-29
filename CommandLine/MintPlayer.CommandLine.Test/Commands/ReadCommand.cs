using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Commands;

[CommandOption<Options.FileOption>(Required = true)]
[CommandOption<Options.DelayOption>(Required = false)]
[SubCommand<Read.ReadSizeCommand>]
internal class ReadCommand : ICommand<ReadCommandInput>
{
    public string Name => "read";

    public string Description => "Read a file";
}

internal partial class ReadCommandInput
{

}