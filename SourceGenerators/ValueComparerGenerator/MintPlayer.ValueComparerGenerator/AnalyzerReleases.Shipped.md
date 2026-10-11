; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

; Releases before 12.1.1 were not tracked. This section is the baseline: every rule in the
; published MintPlayer.ValueComparerGenerator 12.1.1 package.

## Release 12.1.1

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|-------
MINT001 | Correctness            | Error    | RoslynTypeInModelAnalyzer
MINT002 | ValueComparerGenerator | Info     | Equality member already declared
MINT003 | ValueComparerGenerator | Error    | [GenerateEquality] type must be partial
MINT004 | ValueComparerGenerator | Error    | Invalid [UseEqualityComparer] type
MINT005 | ValueComparerGenerator | Warning  | Property type has only reference equality
MINT006 | ValueComparerGenerator | Error    | Containing type must be partial
