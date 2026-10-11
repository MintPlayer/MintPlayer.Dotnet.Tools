; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MAPC001 | MapperGenerator | Error | DiagnosticRules
MAPC002 | MapperGenerator | Error | DiagnosticRules
MAPC003 | MapperGenerator | Error | DiagnosticRules
MAPC004 | MapperGenerator | Error | DiagnosticRules
MAPC005 | MapperGenerator | Error | DiagnosticRules, a [GenerateMapper] type that cannot be instantiated (interface, abstract, static, ref struct, open generic)
MAPC006 | MapperGenerator | Error | DiagnosticRules, a [GenerateMapper] type the generated mapper cannot access (private, protected or file-local)
MAPC007 | MapperGenerator | Error | DiagnosticRules, a [MapperConversion] method the generated mapper cannot call (generic, or in an inaccessible class)
