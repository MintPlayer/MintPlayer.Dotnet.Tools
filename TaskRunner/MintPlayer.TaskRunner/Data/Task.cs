using MintPlayer.TaskRunner.Enums;

namespace MintPlayer.TaskRunner.Data;

internal class Task
{
    public ETaskType Type { get; set; }
    public string? Command { get; set; }
    public string[]? Args { get; set; }


    // Type = Typescript
    public string? Tsconfig { get; set; }


    // Type = Npm
    public string? Script { get; set; }


    public string? Group { get; set; }
    public string? Label { get; set; }
    public string[]? DependsOn { get; set; }

    public override string ToString() => Label ?? base.ToString();
}
