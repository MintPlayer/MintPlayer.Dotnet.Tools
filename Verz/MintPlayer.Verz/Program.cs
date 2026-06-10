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
using MintPlayer.Verz.Helpers;
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

        builder.Services.AddSingleton<VerzConfigProvider>();
        builder.Services.AddSingleton<PluginLoader>();
        builder.Services.AddSingleton<PluginCatalogProvider>();
        builder.Services.AddSingleton<ProjectGraphBuilder>();
        builder.Services.AddSingleton<VersionPlanner>();

        builder.Services.AddSingleton<GitClient>();
        builder.Services.AddSingleton<InitCommand>();
        builder.Services.AddSingleton<SetVersionsCommand>();
        builder.Services.AddSingleton<CreateTagCommand>();
        builder.Services.AddSingleton<PublishCommand>();

        return builder.Build();
    }

    private static RootCommand BuildCommandTree(IServiceProvider services)
    {
        var root = new RootCommand(
            "Verz — derives library versions from git tags, stamps them at build time, " +
            "and publishes packages via a plugin model.");

        root.AddCommand(BuildInitCommand(services));
        root.AddCommand(BuildSetVersionsCommand(services));
        root.AddCommand(BuildCreateTagCommand(services));
        root.AddCommand(BuildPublishCommand(services));

        return root;
    }

    private static Command BuildPublishCommand(IServiceProvider services)
    {
        var configuration = new Option<string>(
            name: "--configuration",
            description: "Build configuration to pack. Default: Release.",
            getDefaultValue: () => "Release");

        var registry = new Option<string[]>(
            name: "--registry",
            description: "Limit publishing to listed registry IDs. Repeatable.")
        {
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore,
        };

        var cmd = new Command("publish",
            "Pack and push every package tagged at HEAD to each configured registry.");
        cmd.AddOption(configuration);
        cmd.AddOption(registry);
        cmd.SetHandler(async ctx =>
        {
            var opts = new PublishOptions(
                Configuration: ctx.ParseResult.GetValueForOption(configuration) ?? "Release",
                Registries: ctx.ParseResult.GetValueForOption(registry));

            var handler = services.GetRequiredService<PublishCommand>();
            ctx.ExitCode = await handler.HandleAsync(opts, ctx.GetCancellationToken());
        });
        return cmd;
    }

    private static Command BuildCreateTagCommand(IServiceProvider services)
    {
        var dryRun = new Option<bool>("--dry-run",
            description: "Print the planned tags without creating or pushing them.");
        var push = new Option<bool>("--push",
            description: "After creating local tags, git push --tags to the remote.");
        var remote = new Option<string>("--remote",
            description: "Git remote name for --push. Default: origin.",
            getDefaultValue: () => "origin");
        var configuration = new Option<string>("--configuration",
            description: "Build configuration whose bin output is hashed. Default: Release.",
            getDefaultValue: () => "Release");

        var cmd = new Command("create-tag",
            "Compute next version(s) for affected projects and create tags.");
        cmd.AddOption(dryRun);
        cmd.AddOption(push);
        cmd.AddOption(remote);
        cmd.AddOption(configuration);
        cmd.SetHandler(async ctx =>
        {
            var opts = new CreateTagOptions(
                DryRun: ctx.ParseResult.GetValueForOption(dryRun),
                Push: ctx.ParseResult.GetValueForOption(push),
                Remote: ctx.ParseResult.GetValueForOption(remote) ?? "origin",
                Configuration: ctx.ParseResult.GetValueForOption(configuration) ?? "Release");

            var handler = services.GetRequiredService<CreateTagCommand>();
            ctx.ExitCode = await handler.HandleAsync(opts, ctx.GetCancellationToken());
        });
        return cmd;
    }

    private static Command BuildSetVersionsCommand(IServiceProvider services)
    {
        var refOption = new Option<string>(
            name: "--ref",
            description: "Git ref to read tags from. Default: HEAD.",
            getDefaultValue: () => "HEAD");

        var dryRun = new Option<bool>(
            name: "--dry-run",
            description: "Print the planned changes without modifying files.");

        var cmd = new Command("set-versions", "Apply tag-derived versions to discovered projects.");
        cmd.AddOption(refOption);
        cmd.AddOption(dryRun);
        cmd.SetHandler(async ctx =>
        {
            var opts = new SetVersionsOptions(
                Ref: ctx.ParseResult.GetValueForOption(refOption) ?? "HEAD",
                DryRun: ctx.ParseResult.GetValueForOption(dryRun));

            var handler = services.GetRequiredService<SetVersionsCommand>();
            ctx.ExitCode = await handler.HandleAsync(opts, ctx.GetCancellationToken());
        });
        return cmd;
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
