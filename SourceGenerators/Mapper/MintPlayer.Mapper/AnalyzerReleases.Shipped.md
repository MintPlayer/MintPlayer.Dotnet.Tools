; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

; Releases before 12.1.1 were not tracked. This section is the baseline: every rule in the
; published MintPlayer.Mapper 12.1.1 package. (MAPC003 and MAPC004 shipped in 7.4.0/7.4.1 and were
; gone again in 7.5.0, so they are not part of the baseline.)

## Release 12.1.1

### New Rules

Rule ID | Category        | Severity | Notes
--------|-----------------|----------|-------
MAPC001 | MapperGenerator | Error    | Assembly-level [GenerateMapper] must have 2 types as parameters
MAPC002 | MapperGenerator | Error    | Type-level [GenerateMapper] must have 1 type as parameter
