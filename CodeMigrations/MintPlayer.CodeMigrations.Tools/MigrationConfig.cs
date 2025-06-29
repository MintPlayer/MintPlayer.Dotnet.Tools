namespace MintPlayer.CodeMigrations.Tools;

public class MigrationConfig
{
    /// <summary>
    /// Name of the nuget-package containing the migrations.
    /// </summary>
    public required string PackageName { get; init; }
}
