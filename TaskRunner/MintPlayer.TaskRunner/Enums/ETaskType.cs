namespace MintPlayer.TaskRunner.Enums;

internal enum ETaskType
{
    Grunt,
    Gulp,
    Jake,
    Npm,

    /// <summary>Defines whether the task is run as a process or as a command inside a shell.</summary>
    Process,

    /// <summary>Defines whether the task is run as a process or as a command inside a shell.</summary>
    Shell,
    Typescript,
}
