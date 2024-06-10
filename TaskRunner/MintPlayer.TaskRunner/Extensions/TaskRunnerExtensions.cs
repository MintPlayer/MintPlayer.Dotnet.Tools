using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.TaskRunner.Extensions;

public static class TaskRunnerExtensions
{
    public static IServiceCollection AddTaskRunner(this IServiceCollection services)
    {
        return services
            .AddScoped<ITaskRunner, TaskRunner>();
    }
}
