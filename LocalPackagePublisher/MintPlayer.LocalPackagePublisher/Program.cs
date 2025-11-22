using Microsoft.Extensions.Hosting;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.LocalPackagePublisher;
using MintPlayer.SourceGenerators.Attributes;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddPublishLocalCommand()
    .AddServices();

var app = builder.Build();
try
{
    var exitCode = await app.InvokePublishLocalCommandAsync(args);
    return exitCode;
}
catch (ParseCommandException parseEx)
{
    throw;
}


namespace MintPlayer.LocalPackagePublisher
{
    [CliRootCommand(
        Name = "publishlocal",
        Description = "Pack & publish all .csproj in the current directory to a local NuGet source.")]
    public partial class PublishLocalCommand : ICliCommand
    {
        [CliOption(["--source", "-s"], Description = "Name of the local NuGet source (from nuget.config).", Required = true), NoInterfaceMember]
        public string SourceName { get; set; } = null!;

        [CliOption(["--version", "-v"], Description = "Version to apply to all packages.", Required = true), NoInterfaceMember]
        public string Version { get; set; } = null!;

        [CliOption(["--dry-run"], Description = "Show what would be done, without packing or copying packages."), NoInterfaceMember]
        public bool DryRun { get; set; }

        [Inject] private readonly IPackagePublisher publisher;

        public async Task<int> Execute(CancellationToken cancellationToken)
        {
            try
            {
                await publisher.RunAsync(SourceName, Version, DryRun);
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.Message);
                Console.ResetColor();
                return 1;
            }
        }
    }
}