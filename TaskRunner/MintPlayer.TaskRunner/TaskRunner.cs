using MintPlayer.TaskRunner.Data;
using Newtonsoft.Json;
using System.Diagnostics;

namespace MintPlayer.TaskRunner;

public interface ITaskRunner
{
    System.Threading.Tasks.Task RunTasks(string tasksJsonPath, CancellationTokenSource cts);
}

internal class TaskRunner : ITaskRunner
{
    public System.Threading.Tasks.Task RunTasks(string tasksJsonPath, CancellationTokenSource cts)
    {
        var contents = File.ReadAllText(tasksJsonPath);
        var tasksJson = JsonConvert.DeserializeObject<Data.TasksJson>(contents);
        if (tasksJson == null) throw new InvalidOperationException();

        var tasksDictionary = tasksJson.Tasks.ToDictionary(t => t.Label, t => new InternalTask(t));

        // Remap each task dependency
        Array.ForEach(tasksDictionary.ToArray(), t =>
        {
            if (t.Value.Task.DependsOn != null)
            {
                t.Value.DependsOn = t.Value.Task.DependsOn.Select(d => tasksDictionary[d]).ToArray();
            }
        });


        return System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task RunTask(Data.Task task, CancellationTokenSource cts)
    {
        switch (task.Type)
        {
            case Enums.ETaskType.Shell:
                var process = Process.Start(task.Command, string.Join(" ", task.Args));
                process.Exited += (sender, e) => { };

                // Cancel => Kill process
                CancellationTokenRegistration reg;
                reg = cts.Token.Register(() =>
                {
                    if (!process.HasExited) process.Kill();
                    reg.Dispose();
                });

                break;
            case Enums.ETaskType.Npm:
                break;
            case Enums.ETaskType.Typescript:
                break;
            default:
                throw new NotImplementedException();
        }
    }
}
