using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SlnLaunch.Commands;

[CliRootCommand(Name = "slnlaunch", Description = "Run a Visual Studio .slnLaunch multi-project launch profile from the command line.")]
public partial class SlnLaunchCommand : ICliCommand
{
    [Inject] private readonly IConsoleService _console;
    [Inject] private readonly ISlnLaunchFileService _files;
    [Inject] private readonly ILaunchPlanBuilder _builder;
    [Inject] private readonly IProcessOrchestrator _orchestrator;
    [Inject] private readonly ForwardableArguments _forwardable;

    [CliArgument(0, Name = "file", Required = false, Description = "Path to a .slnLaunch file. Auto-discovered in the current directory when omitted."), NoInterfaceMember]
    public string? FilePath { get; set; }

    [CliOption("--profile", "-p", Description = "Profile to launch (required when the file has more than one)."), NoInterfaceMember]
    public string? Profile { get; set; }

    [CliOption("--list", "-l", Description = "List the profiles in the file and exit."), NoInterfaceMember]
    public bool List { get; set; }

    [CliOption("--watch", Description = "Use 'dotnet watch' instead of 'dotnet run'."), NoInterfaceMember]
    public bool Watch { get; set; }

    [CliOption("--configuration", "-c", Description = "Build configuration, forwarded to every project."), NoInterfaceMember]
    public string? Configuration { get; set; }

    [CliOption("--framework", "-f", Description = "Target framework, forwarded to every project."), NoInterfaceMember]
    public string? Framework { get; set; }

    [CliOption("--no-build", Description = "Forward --no-build to every project."), NoInterfaceMember]
    public bool NoBuild { get; set; }

    [CliOption("--verbosity", "-v", Description = "Verbosity, forwarded to every project."), NoInterfaceMember]
    public string? Verbosity { get; set; }

    [CliOption("--no-prefix", Description = "Don't prefix child output with the project label."), NoInterfaceMember]
    public bool NoPrefix { get; set; }

    [CliOption("--kill-on-fail", Description = "Tear down all projects if any one exits non-zero."), NoInterfaceMember]
    public bool KillOnFail { get; set; }

    [CliOption("--dry-run", Description = "Print the dotnet commands that would run, then exit."), NoInterfaceMember]
    public bool DryRun { get; set; }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        var path = ResolveFile();
        if (path is null)
            return 1;

        SlnLaunchFile file;
        try
        {
            file = _files.Load(path);
        }
        catch (SlnLaunchException ex)
        {
            _console.WriteError(ex.Message);
            return 1;
        }

        if (List)
        {
            PrintList(file);
            return 0;
        }

        var profile = SelectProfile(file);
        if (profile is null)
            return 1;

        LaunchPlan plan;
        try
        {
            var options = new LaunchPlanOptions
            {
                Watch = Watch,
                Configuration = Configuration,
                Framework = Framework,
                NoBuild = NoBuild,
                Verbosity = Verbosity,
                ForwardableArguments = _forwardable,
            };
            plan = _builder.Build(profile, file.Directory, options);
        }
        catch (SlnLaunchException ex)
        {
            _console.WriteError(ex.Message);
            return 1;
        }

        foreach (var warning in plan.Warnings)
            _console.WriteWarning(warning);

        if (plan.Commands.Count == 0)
        {
            _console.WriteWarning($"Profile '{plan.ProfileName}' has no projects to launch.");
            return 0;
        }

        if (DryRun)
        {
            _console.WriteInfo($"Profile '{plan.ProfileName}' would launch {plan.Commands.Count} project(s):");
            foreach (var command in plan.Commands)
                _console.WriteLine($"  [{command.Label}] {command.ToDisplayString()}");
            return 0;
        }

        _console.WriteInfo($"Launching profile '{plan.ProfileName}' ({plan.Commands.Count} project(s)). Press Ctrl+C to stop.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var signals = new SignalScope(cts, _console);

        var runOptions = new LaunchRunOptions { NoPrefix = NoPrefix, KillOnFail = KillOnFail };
        return await _orchestrator.RunAsync(plan, runOptions, cts.Token);
    }

    private string? ResolveFile()
    {
        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            if (!System.IO.File.Exists(FilePath))
            {
                _console.WriteError($"File not found: {FilePath}");
                return null;
            }
            return FilePath;
        }

        var found = _files.Find(Directory.GetCurrentDirectory());
        if (found.Count == 0)
        {
            _console.WriteError("No .slnLaunch file found in the current directory. Pass one explicitly: slnlaunch <file>.");
            return null;
        }
        if (found.Count > 1)
        {
            _console.WriteError("Multiple .slnLaunch files found here; pass the one you want:");
            foreach (var candidate in found)
                _console.WriteLine($"  {System.IO.Path.GetFileName(candidate)}");
            return null;
        }

        return found[0];
    }

    private LaunchProfile? SelectProfile(SlnLaunchFile file)
    {
        if (!string.IsNullOrWhiteSpace(Profile))
        {
            var match = file.Profiles.FirstOrDefault(p => string.Equals(p.Name, Profile, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _console.WriteError($"Profile '{Profile}' not found. Available: {string.Join(", ", file.Profiles.Select(p => $"'{p.Name}'"))}.");
                return null;
            }
            return match;
        }

        if (file.Profiles.Count == 1)
            return file.Profiles[0];

        _console.WriteError("This file has multiple profiles; choose one with --profile:");
        foreach (var profile in file.Profiles)
            _console.WriteLine($"  {profile.Name}");
        return null;
    }

    private void PrintList(SlnLaunchFile file)
    {
        _console.WriteInfo(file.FilePath);
        foreach (var profile in file.Profiles)
        {
            _console.WriteLine($"  {profile.Name}");
            foreach (var project in profile.Projects)
            {
                var target = string.IsNullOrEmpty(project.DebugTarget) ? string.Empty : $" ({project.DebugTarget})";
                _console.WriteLine($"    - {project.Path} [{project.Action}]{target}");
            }
        }
    }
}
