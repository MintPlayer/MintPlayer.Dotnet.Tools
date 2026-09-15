using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public static class DiagnosticDescriptorExtensions
{
    public static Diagnostic Create(this DiagnosticDescriptor descriptor, Location? location, string[]? messageArgs = null)
        => Diagnostic.Create(descriptor, location, messageArgs);
}
