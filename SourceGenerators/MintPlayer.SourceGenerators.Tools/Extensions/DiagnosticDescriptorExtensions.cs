using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class DiagnosticDescriptorExtensions
{
    public static Diagnostic Create(this DiagnosticDescriptor descriptor, Location? location, params object[] messageArgs)
        => Diagnostic.Create(descriptor, location, messageArgs);
}
