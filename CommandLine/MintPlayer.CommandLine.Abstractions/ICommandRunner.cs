namespace MintPlayer.CommandLine.Abstractions;

public interface ICommandRunner
{
    //Task Run<TCommand>()
    //    where TCommand : ICommand;

    //Task Run<TCommand>()
    //    where TCommand : ICommand<TInput>;

    CommandExecutor<TCommand, TResult> Get<TCommand, TResult>();
}

//public interface ICommandExecutor<TCommand>
//{
//    Task Execute();
//}

//public interface ICommandExecutor<TCommand, TInput>
//{
//    Task Execute(TInput input);
//}

//public interface ICommandExecutor<TCommand, TInput, TResult>
//{
//    Task<TResult> Execute(TInput input);
//}


public class CommandExecutor<TCommand, TResult>
{
    public async Task<TResult?> Execute<TInput>(TInput input)
    {
        throw new NotImplementedException();
    }
}
