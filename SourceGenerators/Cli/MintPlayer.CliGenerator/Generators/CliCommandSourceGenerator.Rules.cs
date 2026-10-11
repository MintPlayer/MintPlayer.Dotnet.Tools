using Microsoft.CodeAnalysis;

namespace MintPlayer.CliGenerator.Generators;

public sealed partial class CliCommandSourceGenerator
{
    private const string Category = "MintPlayer.CliGenerator";

    internal static readonly DiagnosticDescriptor CommandMustBePartial = new(
        id: "MINTCLI002",
        title: "CLI command must be partial",
        messageFormat: "CLI command '{0}' must be declared partial: the generator adds its command-building members to it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CommandCannotBeGeneric = new(
        id: "MINTCLI003",
        title: "CLI command cannot be generic",
        messageFormat: "CLI command '{0}' cannot be generic or nested in a generic type: the generated code has no type arguments to construct it with",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CommandCannotBeStatic = new(
        id: "MINTCLI004",
        title: "CLI command cannot be static",
        messageFormat: "CLI command '{0}' cannot be static: it is resolved from dependency injection and its Execute method is called on the instance",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        id: "MINTCLI005",
        title: "Type containing a CLI command must be partial",
        messageFormat: "Type '{1}', which contains the CLI command '{0}', must be a partial class, record, struct or interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor CommandCannotBeFileLocal = new(
        id: "MINTCLI006",
        title: "CLI command cannot be file-local",
        messageFormat: "CLI command '{0}' cannot be file-local or nested in a file-local type: a partial declared in a generated file would be a different type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RootCommandNotAccessible = new(
        id: "MINTCLI007",
        title: "Root command must be accessible from the whole assembly",
        messageFormat: "Root command '{0}' must be internal or public, and so must its containing types: the generated '{1}CliExtensions' class calls into it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SubcommandNotAccessible = new(
        id: "MINTCLI008",
        title: "Subcommand must be accessible from its parent command",
        messageFormat: "Subcommand '{0}' is not accessible from its parent command '{1}', which registers and builds it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateRootCommandName = new(
        id: "MINTCLI009",
        title: "Root commands must have distinct type names",
        messageFormat: "Root command '{0}' has the same type name as '{1}', so both would get a '{2}CliExtensions' class; rename one of them. No extensions are generated for either.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
