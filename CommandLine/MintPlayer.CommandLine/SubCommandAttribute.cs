using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class SubCommandAttribute<TCommand> : Attribute
    where TCommand : class, ICommand
{
}
