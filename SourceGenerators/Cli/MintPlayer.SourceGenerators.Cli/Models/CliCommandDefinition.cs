using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Cli.Models;

internal sealed record CliCommandDefinition(
    string Namespace,
    string Declaration,
    string TypeName,
    string FullyQualifiedName,
    string? ParentFullyQualifiedName,
    bool IsRoot,
    string? CommandName,
    string? Description,
    bool HasHandler,
    string? HandlerMethodName,
    CliHandlerReturnKind HandlerReturnKind,
    bool HandlerUsesCancellationToken,
    ImmutableArray<CliOptionDefinition> Options,
    ImmutableArray<CliArgumentDefinition> Arguments);
