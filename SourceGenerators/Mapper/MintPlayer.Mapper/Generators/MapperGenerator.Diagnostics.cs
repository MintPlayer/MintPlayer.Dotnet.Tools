using Microsoft.CodeAnalysis;
using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.Mapper.Generators;

public class ConversionMethodMissingStateDiagnostic : IDiagnosticReporter
{
    private readonly IEnumerable<ConversionMethod> conversionMethods;
    public ConversionMethodMissingStateDiagnostic(IEnumerable<ConversionMethod> conversionMethods)
    {
        this.conversionMethods = conversionMethods;
    }

    public IEnumerable<Diagnostic> GetDiagnostics()
    {
        return conversionMethods
            .Where(cm => cm.SourceState is null || cm.DestinationState is null)
            .Select(cm => DiagnosticRules.ConversionMethodMissingStateRule.Create(cm.AttributeLocation ?? Location.None));
    }
}

public class ConversionMethodUnnecessaryStateDiagnostic : IDiagnosticReporter
{
    private readonly IEnumerable<ConversionMethod> conversionMethods;
    public ConversionMethodUnnecessaryStateDiagnostic(IEnumerable<ConversionMethod> conversionMethods)
    {
        this.conversionMethods = conversionMethods;
    }

    public IEnumerable<Diagnostic> GetDiagnostics()
    {
        return conversionMethods
            .Where(cm => cm.SourceState is null || cm.DestinationState is null)
            .Select(cm => DiagnosticRules.ConversionMethodUnnecessaryStateRule.Create(cm.AttributeLocation ?? Location.None));
    }
}
