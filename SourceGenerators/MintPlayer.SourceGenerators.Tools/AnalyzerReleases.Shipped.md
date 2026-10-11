; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

; Releases before 12.1.1 were not tracked. This section is the baseline: every rule in the
; published MintPlayer.SourceGenerators.Tools 12.1.1 package. These descriptors are reported by every
; generator built on Tools, not by Tools itself.

## Release 12.1.1

### New Rules

Rule ID | Category                    | Severity | Notes
--------|-----------------------------|----------|-------
MPSG001 | MintPlayer.SourceGenerators | Error    | Source generator failed to produce a file
