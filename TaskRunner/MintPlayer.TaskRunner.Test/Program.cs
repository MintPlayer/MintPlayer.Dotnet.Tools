using Microsoft.Extensions.DependencyInjection;
using MintPlayer.TaskRunner.Extensions;

namespace MintPlayer.TaskRunner.Test;

internal class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection()
            .AddTaskRunner()
            .BuildServiceProvider();

        var taskRunner = services.GetRequiredService<ITaskRunner>();
        await taskRunner.RunTasks("tasks.json");
    }
}
