using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.CliGenerator.Extensions;
using MintPlayer.CliGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using System.Collections.Immutable;

namespace MintPlayer.CliGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public sealed partial class CliCommandSourceGenerator : IncrementalGenerator
{
    internal const string RootCommandAttributeName = "MintPlayer.CliGenerator.Attributes.CliRootCommandAttribute";
    internal const string CommandAttributeName = "MintPlayer.CliGenerator.Attributes.CliCommandAttribute";
    internal const string ParentCommandAttributeName = "MintPlayer.CliGenerator.Attributes.CliParentCommandAttribute";
    internal const string OptionAttributeName = "MintPlayer.CliGenerator.Attributes.CliOptionAttribute";
    internal const string ArgumentAttributeName = "MintPlayer.CliGenerator.Attributes.CliArgumentAttribute";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        // ForAttributeWithMetadataName visits only declarations that carry the attribute, so unrelated code (a
        // record struct, an interface, a [GeneratedRegex] partial) never reaches the transform. A class with both
        // attributes is discovered by the root pass only.
        var rootDiscovery = context.SyntaxProvider.ForAttributeWithMetadataName(
            RootCommandAttributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, ct) => Discover(ctx, isRootPass: true, ct));
        var commandDiscovery = context.SyntaxProvider.ForAttributeWithMetadataName(
            CommandAttributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, ct) => Discover(ctx, isRootPass: false, ct));

        var rootDiscovered = rootDiscovery.Select(static (d, _) => d.Discovered);
        var commandDiscovered = commandDiscovery.Select(static (d, _) => d.Discovered);

        // BuildCommandTrees builds a new collection on every run, so it is an EquatableArray: an
        // ImmutableArray compares by reference and would report Modified even when the trees are equal.
        var commandTreesProvider = rootDiscovered.Models().Collect()
            .Combine(commandDiscovered.Models().Collect())
            .Select(static (definitions, _) => BuildCommandTrees(definitions.Left.AddRange(definitions.Right)).ToEquatableArray());

        var producerProvider = commandTreesProvider
            .Combine(settingsProvider)
            .Select(static Producer (tuple, _) =>
            {
                var rootNamespace = tuple.Right.RootNamespace ?? string.Empty;
                var trees = tuple.Left.AsImmutableArray();
                return new CliCommandProducer(trees, rootNamespace);
            });

        // Root names carry a location, so they stay out of the models: an edit that only moves a root command
        // re-runs this check, not the producer.
        var duplicateRootDiagnostics = rootDiscovery
            .Select(static (d, _) => d.Root)
            .Where(static root => root is not null)
            .Collect()
            .SelectMany(static (roots, _) => FindDuplicateRootNames(roots!));

        context.ProduceCode(producerProvider);
        context.ReportDiagnostics(rootDiscovered.Diagnostics(), commandDiscovered.Diagnostics(), duplicateRootDiagnostics);
    }

    private static (Discovered<CliCommandDefinition> Discovered, CliRootName? Root) Discover(GeneratorAttributeSyntaxContext context, bool isRootPass, CancellationToken cancellationToken)
    {
        // The attributes are AttributeTargets.Class: on anything else the compiler already reports CS0592.
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol || classSymbol.TypeKind != TypeKind.Class)
        {
            return (Discovered<CliCommandDefinition>.None, null);
        }

        var rootAttribute = classSymbol.FindAttribute(RootCommandAttributeName);
        var commandAttribute = classSymbol.FindAttribute(CommandAttributeName);
        if (!isRootPass && rootAttribute is not null)
        {
            return (Discovered<CliCommandDefinition>.None, null);
        }

        var compilation = context.SemanticModel.Compilation;
        var location = classSymbol.Locations.FirstOrDefault();
        var displayName = classSymbol.ToDisplayString();
        var pathSpec = classSymbol.GetPathSpec(cancellationToken)!;
        var diagnostics = new List<DiagnosticInfo>();

        if (classSymbol.IsStatic)
        {
            diagnostics.Add(DiagnosticInfo.Create(CommandCannotBeStatic, location, displayName));
        }

        if (classSymbol.IsGenericType)
        {
            diagnostics.Add(DiagnosticInfo.Create(CommandCannotBeGeneric, location, displayName));
        }

        switch (pathSpec.UnsupportedReason)
        {
            case EPathSpecUnsupportedReason.TargetNotPartial:
                diagnostics.Add(DiagnosticInfo.Create(CommandMustBePartial, location, displayName));
                break;
            case EPathSpecUnsupportedReason.ParentNotPartial:
            case EPathSpecUnsupportedReason.UnsupportedKind:
                diagnostics.Add(DiagnosticInfo.Create(ContainingTypeMustBePartial, location, displayName, pathSpec.UnsupportedTypeName));
                break;
            case EPathSpecUnsupportedReason.FileLocal:
                diagnostics.Add(DiagnosticInfo.Create(CommandCannotBeFileLocal, location, displayName));
                break;
        }

        // 1. Explicit parent via [CliParentCommand(typeof(ParentType))]
        // 2. Fallback to the containing type (nested classes)
        INamedTypeSymbol? parentSymbol = null;
        if (classSymbol.FindAttribute(ParentCommandAttributeName).TryGetArgument<INamedTypeSymbol>("parentCommandType", out var explicitParent)
            && IsCommand(explicitParent))
        {
            parentSymbol = explicitParent;
        }
        else if (classSymbol.ContainingType is { } containingType && IsCommand(containingType))
        {
            parentSymbol = containingType;
        }

        var isRoot = rootAttribute is not null;
        if (isRoot && !compilation.IsSymbolAccessibleWithin(classSymbol, compilation.Assembly))
        {
            diagnostics.Add(DiagnosticInfo.Create(RootCommandNotAccessible, location, displayName, classSymbol.Name));
        }

        if (parentSymbol is not null && !compilation.IsSymbolAccessibleWithin(classSymbol, parentSymbol))
        {
            diagnostics.Add(DiagnosticInfo.Create(SubcommandNotAccessible, location, displayName, parentSymbol.ToDisplayString()));
        }

        if (diagnostics.Count > 0)
        {
            return (Discovered<CliCommandDefinition>.Fail(diagnostics.ToArray()), null);
        }

        // A named argument wins over the constructor argument of the same name, as it does at run time.
        string? commandName = null;
        string? description = null;
        if (rootAttribute is not null)
        {
            commandName = rootAttribute.GetArgument<string?>("Name", null);
            description = rootAttribute.GetArgument<string?>("Description", null);
        }

        if (commandAttribute is not null)
        {
            commandName ??= commandAttribute.GetArgument<string?>("Name", null);
            description ??= commandAttribute.GetArgument<string?>("Description", null);
        }

        if (!isRoot && string.IsNullOrWhiteSpace(commandName))
        {
            commandName = classSymbol.Name.ToKebabCase();
        }

        var properties = classSymbol.GetMembers().OfType<IPropertySymbol>().Where(static p => !p.IsStatic).ToArray();

        var optionDefinitions = properties
            .Select(ToOptionDefinition)
            .Where(static option => option is not null)
            .Select(static option => option!)
            .ToImmutableArray();

        var argumentDefinitions = properties
            .Select(ToArgumentDefinition)
            .Where(static argument => argument is not null)
            .Select(static argument => argument!)
            .OrderBy(argument => argument.Position)
            .ToImmutableArray();

        var fullyQualifiedName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var definition = new CliCommandDefinition
        {
            Namespace = pathSpec.ContainingNamespace ?? string.Empty,
            TypeName = classSymbol.Name,
            FullyQualifiedName = fullyQualifiedName,
            ParentFullyQualifiedName = parentSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            PathSpec = pathSpec,
            IsRoot = isRoot,
            CommandName = commandName,
            Description = description,
            Options = optionDefinitions,
            Arguments = argumentDefinitions,
        };

        var root = isRoot
            ? new CliRootName { TypeName = classSymbol.Name, DisplayName = displayName, Location = location.AsKey() }
            : null;

        return (new Discovered<CliCommandDefinition>(definition), root);
    }

    private static bool IsCommand(INamedTypeSymbol type)
        => type.HasAttribute(RootCommandAttributeName) || type.HasAttribute(CommandAttributeName);

    /// <summary>
    /// Root commands share one extensions namespace, so two roots with the same type name would both declare
    /// <c>{Name}CliExtensions</c> (CS0101). Each one gets a diagnostic, and the producer writes no extensions for either.
    /// </summary>
    private static ImmutableArray<DiagnosticInfo> FindDuplicateRootNames(ImmutableArray<CliRootName> roots)
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var group in roots.GroupBy(static root => root.TypeName, StringComparer.Ordinal).Where(static g => g.Count() > 1))
        {
            foreach (var root in group)
            {
                var others = string.Join("', '", group.Where(other => !ReferenceEquals(other, root)).Select(static other => other.DisplayName));
                builder.Add(new DiagnosticInfo(DuplicateRootCommandName, root.Location, new[] { root.DisplayName, others, root.TypeName }));
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<CliCommandTree> BuildCommandTrees(ImmutableArray<CliCommandDefinition> definitions)
    {
        if (definitions.IsDefaultOrEmpty)
        {
            return ImmutableArray<CliCommandTree>.Empty;
        }

        var deduplicated = new Dictionary<string, CliCommandDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            deduplicated[definition.FullyQualifiedName] = definition;
        }

        var nodeLookup = deduplicated.Values
            .ToDictionary(definition => definition.FullyQualifiedName, definition => new CommandNode(definition), StringComparer.Ordinal);

        foreach (var node in nodeLookup.Values)
        {
            if (node.Definition.ParentFullyQualifiedName is string parentName && nodeLookup.TryGetValue(parentName, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        var roots = nodeLookup.Values
            .Where(node => node.Definition.IsRoot)
            .Where(node => node.Definition.ParentFullyQualifiedName is null || !nodeLookup.ContainsKey(node.Definition.ParentFullyQualifiedName))
            .Select(ToTree)
            .ToImmutableArray();

        return roots;
    }

    private static CliCommandTree ToTree(CommandNode node)
    {
        var children = node.Children.Select(ToTree).ToImmutableArray();
        return new CliCommandTree
        {
            Command = node.Definition,
            Children = children,
        };
    }

    private sealed class CommandNode
    {
        public CommandNode(CliCommandDefinition definition)
        {
            Definition = definition;
        }

        public CliCommandDefinition Definition { get; }
        public List<CommandNode> Children { get; } = new();
    }

    private static CliOptionDefinition? ToOptionDefinition(IPropertySymbol propertySymbol)
    {
        var attribute = propertySymbol.FindAttribute(OptionAttributeName);
        if (attribute is null)
        {
            return null;
        }

        // [CliOption(null)] passes a null params array: no aliases, as with [CliOption].
        var aliases = (attribute.GetArgument<string?[]?>("aliases", null) ?? [])
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias!)
            .ToList();

        if (aliases.Count == 0)
        {
            aliases.Add(ToAlias(propertySymbol.Name));
        }

        var hasDefaultValue = attribute.TryGetArgument<TypedConstant>("DefaultValue", out var defaultConstant)
            && defaultConstant.Kind != TypedConstantKind.Error
            && !defaultConstant.IsNull;

        return new CliOptionDefinition
        {
            PropertyName = propertySymbol.Name,
            PropertyType = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Aliases = aliases,
            Description = attribute.GetArgument<string?>("Description", null),
            Required = attribute.GetArgument("Required", false),
            Hidden = attribute.GetArgument("Hidden", false),
            DefaultValueExpression = hasDefaultValue ? defaultConstant.ToCSharpString() : null,
            HasDefaultValue = hasDefaultValue,
        };
    }

    private static CliArgumentDefinition? ToArgumentDefinition(IPropertySymbol propertySymbol)
    {
        var attribute = propertySymbol.FindAttribute(ArgumentAttributeName);
        if (attribute is null || !attribute.TryGetArgument<int>("position", out var position))
        {
            return null;
        }

        return new CliArgumentDefinition
        {
            PropertyName = propertySymbol.Name,
            PropertyType = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Position = position,
            ArgumentName = attribute.GetArgument<string?>("Name", null) ?? propertySymbol.Name.ToCamelCase(),
            Description = attribute.GetArgument<string?>("Description", null),
            Required = attribute.GetArgument("Required", true),
        };
    }

    private static string ToAlias(string propertyName)
    {
        return "--" + propertyName.ToKebabCase();
    }
}
