using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine.Test.Commands;

[CommandOption<Options.DelayOption>(Required = false)]
public class DelayCommand : ICommand<DelayCommandInput>
{
    public string Name => "delay";

    public string Description => "Waits for a specified time";

    public async Task Execute(DelayCommandInput input)
    {
        var delay = 1000; // input.DelayMilliseconds;
        await Task.Delay(delay);
    }
}

internal partial class DelayCommandInput { }
