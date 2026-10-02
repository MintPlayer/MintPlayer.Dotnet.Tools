using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace S6;

// ---- Runtime package (MintPlayer.Resilience.Extensions) ----

/// <summary>
/// Implemented by the GENERATED part of a reloadable pipeline class. Static abstract members are why the user's
/// class must be a (sealed) non-static partial class: a static class cannot be a type argument (CS0718) nor
/// implement an interface, so <c>AddResiliencePipeline&lt;CatalogPipeline&gt;()</c> would not compile.
/// </summary>
public interface IGeneratedResiliencePipeline
{
    /// <summary>Generated. For a reloadable pipeline it calls <c>AddReloadablePipeline&lt;Self, SelfOptions&gt;</c>;
    /// for a constant one it only adds the registry entry. This is what lets the public call take ONE type argument.</summary>
    static abstract void AddServices(IServiceCollection services, string? sectionPath);
}

public interface IReloadableResiliencePipeline<TOptions> : IGeneratedResiliencePipeline where TOptions : class
{
    static abstract string DefaultSectionPath { get; }

    /// <summary>Validate, then publish a new snapshot. False = rejected, previous snapshot stays live.</summary>
    static abstract bool TryApply(TOptions options, out string? error);
}

public static class ResilienceServiceCollectionExtensions
{
    /// <summary>The public entry point: <c>services.AddResiliencePipeline&lt;CatalogPipeline&gt;()</c>.</summary>
    public static IServiceCollection AddResiliencePipeline<TPipeline>(this IServiceCollection services, string? sectionPath = null)
        where TPipeline : IGeneratedResiliencePipeline
    {
        TPipeline.AddServices(services, sectionPath);
        return services;
    }

    public static IServiceCollection AddReloadablePipeline<TPipeline, TOptions>(this IServiceCollection services, string? sectionPath = null, bool viaMonitor = false)
        where TPipeline : IReloadableResiliencePipeline<TOptions>
        where TOptions : class
    {
        // Idempotent. Without this guard a second call adds a second BindConfiguration → a second
        // IOptionsChangeTokenSource → every reload is applied (and fails) twice. Measured in the first S6 run.
        if (services.Any(d => d.ServiceType == typeof(ReloadBinding<TPipeline, TOptions>) ||
                              d.ServiceType == typeof(MonitorReloadBinding<TPipeline, TOptions>)))
            return services;

        // Deliberately NO OptionsBuilder.Validate(): validation runs in TryApply, so a bad reload is rejected
        // instead of throwing out of the configuration reload.
        services.AddOptions<TOptions>().BindConfiguration(sectionPath ?? TPipeline.DefaultSectionPath);
        if (viaMonitor)
        {
            services.AddSingleton<MonitorReloadBinding<TPipeline, TOptions>>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<MonitorReloadBinding<TPipeline, TOptions>>());
        }
        else
        {
            services.AddSingleton<ReloadBinding<TPipeline, TOptions>>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ReloadBinding<TPipeline, TOptions>>());
        }
        return services;
    }
}

/// <summary>Per-pipeline reload diagnostics (a real package reports these through ILogger + the telemetry listener).</summary>
public static class ReloadDiagnostics<TPipeline>
{
    public static int Failures;
    public static string? LastError;

    public static void Fail(string error) { Interlocked.Increment(ref Failures); LastError = error; }

    /// <summary>Raised after every reload attempt (true = applied). Test hook; the package's equivalent is telemetry.</summary>
    public static event Action<bool>? Reloaded;
    public static void Raise(bool applied) => Reloaded?.Invoke(applied);
}

/// <summary>
/// RECOMMENDED binding. Connects configuration changes to the static generated class without going through
/// IOptionsMonitor: it listens to the same IOptionsChangeTokenSource&lt;TOptions&gt; the monitor would and builds the
/// options with IOptionsFactory inside try/catch, so binder errors (an unconvertible value) and validation errors
/// are both contained: the previous snapshot stays live and nothing throws into the configuration reload.
/// A hosted service so it activates at host start without anyone resolving it; non-host apps call StartAsync (a
/// real package adds <c>IServiceProvider.ActivateResiliencePipelines()</c>). Before activation the pipeline runs on
/// its attribute defaults, so it is never unusable.
/// </summary>
public sealed class ReloadBinding<TPipeline, TOptions>(IOptionsFactory<TOptions> factory, IEnumerable<IOptionsChangeTokenSource<TOptions>> sources)
    : IHostedService, IDisposable
    where TPipeline : IReloadableResiliencePipeline<TOptions>
    where TOptions : class
{
    private readonly List<IDisposable> _registrations = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fail fast at startup (ValidateOnStart semantics); afterwards a bad reload is reported and ignored.
        if (!TPipeline.TryApply(factory.Create(Options.DefaultName), out var error))
            throw new OptionsValidationException(Options.DefaultName, typeof(TOptions), [error!]);

        foreach (var source in sources)
            if ((source.Name ?? Options.DefaultName) == Options.DefaultName)
                _registrations.Add(ChangeToken.OnChange(source.GetChangeToken, Reload));
        return Task.CompletedTask;
    }

    private void Reload()
    {
        var applied = false;
        try
        {
            applied = TPipeline.TryApply(factory.Create(Options.DefaultName), out var error);
            if (!applied) ReloadDiagnostics<TPipeline>.Fail(error!);
        }
        catch (Exception ex) // binder conversion errors, a throwing Configure/PostConfigure
        {
            ReloadDiagnostics<TPipeline>.Fail(ex.Message);
        }
        ReloadDiagnostics<TPipeline>.Raise(applied);
    }

    public Task StopAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }

    public void Dispose()
    {
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
    }
}

/// <summary>
/// The straightforward IOptionsMonitor.OnChange binding, kept to demonstrate its failure mode: OptionsMonitor
/// rebuilds the options BEFORE invoking listeners, so a binder error throws out of IConfigurationRoot.Reload()
/// (an AggregateException) and our listener never runs.
/// </summary>
public sealed class MonitorReloadBinding<TPipeline, TOptions>(IOptionsMonitor<TOptions> monitor) : IHostedService, IDisposable
    where TPipeline : IReloadableResiliencePipeline<TOptions>
    where TOptions : class
{
    private IDisposable? _registration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!TPipeline.TryApply(monitor.CurrentValue, out var error))
            throw new OptionsValidationException(Options.DefaultName, typeof(TOptions), [error!]);
        _registration = monitor.OnChange(static (options, name) =>
        {
            if (name != Options.DefaultName) return;
            if (!TPipeline.TryApply(options, out var error)) ReloadDiagnostics<TPipeline>.Fail(error!);
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }

    public void Dispose() { _registration?.Dispose(); _registration = null; }
}
