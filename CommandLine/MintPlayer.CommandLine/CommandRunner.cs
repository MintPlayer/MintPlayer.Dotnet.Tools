using MintPlayer.CommandLine.Abstractions;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.CommandLine;

[Register(typeof(ICommandRunner), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, "CommandLineTools")]
internal class CommandRunner : ICommandRunner
{
}
