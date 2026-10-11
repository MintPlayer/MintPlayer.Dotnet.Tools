; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

; Releases before 12.1.1 were not tracked. This section is the baseline: every rule in the
; published MintPlayer.CliGenerator 12.1.1 package (MINTCLI001 has shipped since 10.1.0).

## Release 12.1.1

### New Rules

Rule ID    | Category | Severity | Notes
-----------|----------|----------|------------------------------------------------------
MINTCLI001 | Usage    | Error    | CLI commands must implement ICliCommand
