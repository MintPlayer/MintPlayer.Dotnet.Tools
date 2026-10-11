; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID    | Category                | Severity | Notes
-----------|-------------------------|----------|------------------------------------------------------
MINTCLI002 | MintPlayer.CliGenerator | Error    | CLI command must be partial
MINTCLI003 | MintPlayer.CliGenerator | Error    | CLI command cannot be generic
MINTCLI004 | MintPlayer.CliGenerator | Error    | CLI command cannot be static
MINTCLI005 | MintPlayer.CliGenerator | Error    | Type containing a CLI command must be partial
MINTCLI006 | MintPlayer.CliGenerator | Error    | CLI command cannot be file-local
MINTCLI007 | MintPlayer.CliGenerator | Error    | Root command must be accessible from the whole assembly
MINTCLI008 | MintPlayer.CliGenerator | Error    | Subcommand must be accessible from its parent command
MINTCLI009 | MintPlayer.CliGenerator | Error    | Root commands must have distinct type names
