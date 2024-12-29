using MintPlayer.CommandLine.Abstractions;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.CommandLine.Test.Commands.Read;

[CommandOption<Options.FileOption>(Required = true)]
public class ReadSizeCommand : ICommand<ReadSizeCommandInput, ReadSizeCommandOutput>
{
    [Inject] private readonly ICommandRunner commandRunner;

    public string Name => "size";

    public string Description => "Get file size";

    public async Task<ReadCommandOutput> Execute(ReadCommandInput input)
    {
        var output = await commandRunner.Get<ReadCommand, ReadCommandOutput>().Execute(input);
        return new ReadCommandOutput();
    }
}

public partial class ReadSizeCommandInput { }
public class ReadSizeCommandOutput { }