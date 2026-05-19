using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Commands;
using MintPlayer.Verz.Configuration;
using MintPlayer.Verz.Hosting;

namespace MintPlayer.Verz;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = BuildHost();
        var root = BuildCommandTree(host.Services);

        var parser = new CommandLineBuilder(root)
            .UseDefaults()
            .UseExceptionHandler(HandleException, errorExitCode: 1)
            .Build();

        return await parser.InvokeAsync(args);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.IncludeScopes = false;
            o.TimestampFormat = null;
        });

        builder.Services.AddSingleton<PluginLoader>();
        builder.Services.AddSingleton(sp =>
        {
            var loader = sp.GetRequiredService<PluginLoader>();
            return new PluginCatalogProvider(loader, TryLoadConfigFromCwd);
        });

        builder.Services.AddSingleton<InitCommand>();

        return builder.Build();
    }

    private static VerzConfig? TryLoadConfigFromCwd()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "verz.json");
        return File.Exists(path) ? VerzConfigSerializer.Load(path) : null;
    }

    private static RootCommand BuildCommandTree(IServiceProvider services)
    {
        var root = new RootCommand(
            "Verz — derives library versions from git tags, stamps them at build time, " +
            "and publishes packages via a plugin model.");

        root.AddCommand(BuildInitCommand(services));

        return root;
    }

    private static Command BuildInitCommand(IServiceProvider services)
    {
        var stamp = new Option<bool>(
            name: "--stamp-placeholders",
            description: "Insert placeholder versions into discovered .csproj / package.json files.");

        var registry = new Option<string[]>(
            name: "--registry",
            description: "Add a registry entry as id=url. Repeatable.")
        {
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore,
        };

        var init = new Command("init", "Scaffold verz.json in the current directory.");
        init.AddOption(stamp);
        init.AddOption(registry);
        init.SetHandler(async ctx =>
        {
            var opts = new InitOptions(
                StampPlaceholders: ctx.ParseResult.GetValueForOption(stamp),
                Registries: ctx.ParseResult.GetValueForOption(registry) ?? Array.Empty<string>());

            var handler = services.GetRequiredService<InitCommand>();
            ctx.ExitCode = await handler.HandleAsync(opts, ctx.GetCancellationToken());
        });

        return init;
    }

    private static void HandleException(Exception ex, InvocationContext ctx)
    {
        if (ex is VerzException verz)
        {
            ctx.Console.Error.Write(verz.Message + Environment.NewLine);
            ctx.ExitCode = verz.ExitCode;
        }
        else
        {
            ctx.Console.Error.Write($"unhandled: {ex.Message}{Environment.NewLine}");
            ctx.ExitCode = 1;
        }
    }
}
