using MintPlayer.CommandLine.Abstractions;

namespace MintPlayer.CommandLine;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CommandOptionAttribute<TOption> : Attribute
    where TOption : class, ICommandOption
{
    public bool Required { get; set; } = true;
}
