using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.SourceGenerators.Testing;

/// <summary>
/// Supplies the MSBuild-derived options a generator reads, standing in for the ones the real
/// compiler host would pass.
/// </summary>
/// <remarks>
/// Two traps this exists to absorb, both of which fail silently rather than loudly.
///
/// The keys are lowercase — <c>build_property.rootnamespace</c>, not <c>RootNamespace</c> — and
/// Roslyn's real global-options dictionary is case-INSENSITIVE. A case-sensitive dictionary here
/// yields a null RootNamespace, and a producer that passes it on as <c>RootNamespace!</c> then
/// emits a bare <c>namespace</c>, i.e. CS1001 from generated code that looks fine in a snapshot.
///
/// The same options are returned for the global scope, every syntax tree and every additional
/// file. Per-file overrides are a real Roslyn feature, but no generator in practice branches on
/// them, and pretending they do not exist keeps the common case one line.
/// </remarks>
internal sealed class StubAnalyzerConfigOptionsProvider(string? rootNamespace) : AnalyzerConfigOptionsProvider
{
    private readonly StubOptions _options = new(rootNamespace);

    public override AnalyzerConfigOptions GlobalOptions => _options;
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;

    private sealed class StubOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public StubOptions(string? rootNamespace)
        {
            if (!string.IsNullOrEmpty(rootNamespace))
                _values["build_property.rootnamespace"] = rootNamespace!;
        }

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
