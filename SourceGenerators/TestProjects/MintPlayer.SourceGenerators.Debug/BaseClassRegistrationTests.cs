using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SourceGenerators.Debug.BaseClassTests;

#region Non-generic base class registration

/// <summary>
/// A base class simulating a third-party class like Octokit's WebhookEventProcessor.
/// </summary>
public class WebhookEventProcessor
{
    public virtual void ProcessEvent(string payload) { }
}

/// <summary>
/// Registering against a base class (non-generic).
/// Expected generated: .AddScoped&lt;WebhookEventProcessor, MyWebhookEventProcessor&gt;()
/// </summary>
[Register(typeof(WebhookEventProcessor), ServiceLifetime.Scoped)]
internal class MyWebhookEventProcessor : WebhookEventProcessor
{
    public override void ProcessEvent(string payload)
    {
        // Custom processing
    }
}

#endregion

#region Abstract base class registration

public abstract class BaseNotificationService
{
    public abstract Task SendAsync(string message);
}

/// <summary>
/// Registering against an abstract base class.
/// Expected generated: .AddSingleton&lt;BaseNotificationService, EmailNotificationService&gt;()
/// </summary>
[Register(typeof(BaseNotificationService), ServiceLifetime.Singleton)]
internal class EmailNotificationService : BaseNotificationService
{
    public override Task SendAsync(string message) => Task.CompletedTask;
}

#endregion

#region Deep inheritance chain

public class GrandparentService { }
public class ParentService : GrandparentService { }

/// <summary>
/// The specified base class is an ancestor several levels up.
/// Expected generated: .AddScoped&lt;GrandparentService, ChildService&gt;()
/// </summary>
[Register(typeof(GrandparentService), ServiceLifetime.Scoped)]
internal class ChildService : ParentService { }

#endregion

#region Multiple registrations: interface + base class

public interface IProcessor { }
public class BaseProcessor : IProcessor { }

/// <summary>
/// A class registered against both an interface and a base class.
/// Expected generated:
///   .AddScoped&lt;IProcessor, MyProcessor&gt;()
///   .AddScoped&lt;BaseProcessor, MyProcessor&gt;()
/// </summary>
[Register(typeof(IProcessor), ServiceLifetime.Scoped)]
[Register(typeof(BaseProcessor), ServiceLifetime.Scoped)]
internal class MyProcessor : BaseProcessor { }

#endregion

#region Generic base class registration

public class BaseRepository<TEntity> where TEntity : class
{
    public virtual TEntity? FindById(int id) => default;
}

/// <summary>
/// Registering against an open generic base class.
/// Expected generated:
/// <code>
/// public static IServiceCollection AddBaseClassRepositories&lt;TEntity&gt;(this IServiceCollection services)
///     where TEntity : class
/// {
///     return services.AddScoped&lt;BaseRepository&lt;TEntity&gt;, ExtendedRepository&lt;TEntity&gt;&gt;();
/// }
/// </code>
/// </summary>
[Register(typeof(BaseRepository<>), ServiceLifetime.Scoped, "BaseClassRepositories")]
internal class ExtendedRepository<TEntity> : BaseRepository<TEntity> where TEntity : class
{
    public override TEntity? FindById(int id) => default;
}

#endregion
