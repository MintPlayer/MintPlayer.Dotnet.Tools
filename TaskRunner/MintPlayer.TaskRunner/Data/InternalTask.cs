namespace MintPlayer.TaskRunner.Data;

internal class InternalTask
{
    public InternalTask(Data.Task task)
    {
        Task = task;
    }

    public Task Task { get; }
    public InternalTask[]? DependsOn { get; set; }
    public bool HasMasterTask { get; set; }
    public bool IsRunning { get; private set; }

    public override string ToString() => Task.ToString();
}
