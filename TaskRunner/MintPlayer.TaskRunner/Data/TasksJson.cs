namespace MintPlayer.TaskRunner.Data;

internal class TasksJson
{
    public string? Version { get; set; }
    public Task[] Tasks { get; set; } = [];
}
