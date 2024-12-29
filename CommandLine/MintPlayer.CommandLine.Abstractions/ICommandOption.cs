namespace MintPlayer.CommandLine.Abstractions;

public interface ICommandOption
{
    public string Name { get; }
    public string Description { get; }
}

public interface ICommandOption<TValue> : ICommandOption
{
    public TValue GetDefaultValue();
}
