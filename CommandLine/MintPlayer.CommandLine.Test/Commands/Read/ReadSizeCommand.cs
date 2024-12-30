using MintPlayer.CommandLine.Abstractions;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.CommandLine.Test.Commands.Read;

[CommandOption<Options.FileOption>(Required = true)]
public partial class ReadSizeCommand : ICommand<ReadSizeCommandInput, ReadSizeCommandOutput>
{
    [Inject] private readonly ICommandRunner commandRunner;

    // TODO: remove [NoInterfaceMember] attribute
    [NoInterfaceMember] public string Name => "size";

    [NoInterfaceMember] public string Description => "Get file size";


    public async Task<ReadSizeCommandOutput> Execute(ReadSizeCommandInput input)
    {
        await Task.Delay(1);
        //var output = await commandRunner.Get<ReadSizeCommand>().Execute(input);
        return new ReadSizeCommandOutput();
    }
}

public partial class ReadSizeCommandInput { }
public class ReadSizeCommandOutput { }