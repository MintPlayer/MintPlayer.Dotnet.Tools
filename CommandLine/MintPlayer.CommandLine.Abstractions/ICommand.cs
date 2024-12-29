namespace MintPlayer.CommandLine.Abstractions;

public interface ICommand
{
    public string Name { get; }

    public string Description { get; }
}

public interface ICommand<TInput> : ICommand
{
    public Task Execute(TInput input);
}

public interface ICommand<TInput, TResult> : ICommand
{
    public Task<TResult> Execute(TInput input);
}