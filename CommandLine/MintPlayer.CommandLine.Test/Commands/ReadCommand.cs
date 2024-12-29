using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Commands;

[CommandOption<Options.FileOption>(Required = true)]
[CommandOption<Options.DelayOption>(Required = false)]
[SubCommand<Read.ReadSizeCommand>]
internal class ReadCommand : ICommand<ReadCommandInput, ReadCommandOutput>
{
    public string Name => "read";

    public string Description => "Read a file";

    public async Task<ReadCommandOutput> Execute(ReadCommandInput input)
    {
        await Task.Delay(1);
    }
}

internal partial class ReadCommandInput { }
internal class ReadCommandOutput { }