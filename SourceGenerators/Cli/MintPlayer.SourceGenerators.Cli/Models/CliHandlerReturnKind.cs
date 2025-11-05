namespace MintPlayer.SourceGenerators.Cli.Models;

internal enum CliHandlerReturnKind
{
    None,
    Int32,
    Task,
    TaskOfInt32,
    ValueTask,
    ValueTaskOfInt32,
}
