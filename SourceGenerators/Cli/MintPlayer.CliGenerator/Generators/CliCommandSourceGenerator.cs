using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.CliGenerator.Extensions;
using MintPlayer.CliGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace MintPlayer.CliGenerator.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class CliCommandSourceGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> cacheProvider)
    {
        var commandDefinitionsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 }, Transform)
            .Where(static definition => definition is not null)
            .Select(static (definition, _) => definition!)
            .WithComparer()
            .Collect();

        var producerProvider = commandDefinitionsProvider
            .Combine(settingsProvider)
            .Select(static Producer (tuple, _) =>
            {
                var rootNamespace = tuple.Right.RootNamespace ?? string.Empty;
                var trees = BuildCommandTrees(tuple.Left);
                return new CliCommandProducer(trees, rootNamespace);
            });

        context.ProduceCode(producerProvider);
    }

    private static CliCommandDefinition? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return null;
        }

        var semanticModel = context.SemanticModel;
        if (semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        if (classSymbol.TypeKind != TypeKind.Class || classSymbol.IsStatic || classSymbol.IsGenericType)
        {
            return null;
        }

        var compilation = semanticModel.Compilation;
        var rootAttributeSymbol = compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.CliRootCommandAttribute");
        var commandAttributeSymbol = compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.CliCommandAttribute");
        var optionAttributeSymbol = compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.CliOptionAttribute");
        var argumentAttributeSymbol = compilation.GetTypeByMetadataName("MintPlayer.CliGenerator.Attributes.CliArgumentAttribute");
        var cancellationTokenSymbol = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        var taskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");

        if (rootAttributeSymbol is null || commandAttributeSymbol is null || optionAttributeSymbol is null || argumentAttributeSymbol is null)
        {
            return null;
        }

        var attributes = classSymbol.GetAttributes();
        var rootAttribute = attributes.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, rootAttributeSymbol));
        var commandAttribute = attributes.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, commandAttributeSymbol));

        if (rootAttribute is null && commandAttribute is null)
        {
            return null;
        }

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : classSymbol.ContainingNamespace.ToDisplayString();

        var declaration = BuildDeclaration(classSymbol);
        var fullyQualifiedName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string? parentFullyQualifiedName = null;
        if (classSymbol.ContainingType is not null)
        {
            var parentAttributes = classSymbol.ContainingType.GetAttributes();
            var parentIsCommand = parentAttributes.Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, rootAttributeSymbol) ||
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, commandAttributeSymbol));
            if (parentIsCommand)
            {
                parentFullyQualifiedName = classSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        var isRoot = rootAttribute is not null;
        string? commandName = null;
        string? description = null;

        if (rootAttribute is not null)
        {
            commandName = GetStringArgument(rootAttribute, "Name");
            description = GetConstructorArgumentString(rootAttribute, 0) ?? GetStringArgument(rootAttribute, "Description");
        }

        if (commandAttribute is not null)
        {
            commandName ??= GetConstructorArgumentString(commandAttribute, 0);
            description ??= GetStringArgument(commandAttribute, "Description");
        }

        if (!isRoot && string.IsNullOrWhiteSpace(commandName))
        {
            commandName = classSymbol.Name.ToKebabCase();
        }

        var optionDefinitions = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(property => ToOptionDefinition(property, optionAttributeSymbol))
            .Where(static option => option is not null)
            .Select(static option => option!)
            .ToImmutableArray();

        var argumentDefinitions = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(property => ToArgumentDefinition(property, argumentAttributeSymbol))
            .Where(static argument => argument is not null)
            .Select(static argument => argument!)
            .OrderBy(argument => argument.Position)
            .ToImmutableArray();

        var handlerInfo = ResolveHandler(classSymbol, cancellationTokenSymbol, taskSymbol);

        return new CliCommandDefinition
        {
            Namespace = namespaceName,
            Declaration = declaration,
            TypeName = classSymbol.Name,
            FullyQualifiedName = fullyQualifiedName,
            ParentFullyQualifiedName = parentFullyQualifiedName,
            IsRoot = isRoot,
            CommandName = commandName,
            Description = description,
            HasHandler = handlerInfo.HasHandler,
            HandlerMethodName = handlerInfo.MethodName,
            HandlerUsesCancellationToken = handlerInfo.UsesCancellationToken,
            Options = optionDefinitions,
            Arguments = argumentDefinitions,
        };
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

    private static (bool HasHandler, string? MethodName, bool UsesCancellationToken) ResolveHandler(INamedTypeSymbol classSymbol, INamedTypeSymbol? cancellationTokenSymbol, INamedTypeSymbol? taskSymbol)
    {
        var candidates = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(method => !method.IsStatic && (method.Name == "Execute" || method.Name == "ExecuteAsync"))
            .OrderBy(method => method.Name == "Execute" ? 1 : 0);

        foreach (var method in candidates)
        {
            if (method.Parameters.Length > 1)
            {
                continue;
            }

            var usesCancellationToken = method.Parameters.Length == 1;
            if (usesCancellationToken)
            {
                if (cancellationTokenSymbol is null)
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, cancellationTokenSymbol))
                {
                    continue;
                }
            }

            if (!ReturnsTaskOfInt32(method, taskSymbol))
            {
                continue;
            }

            return (true, method.Name, usesCancellationToken);
        }

        return (false, null, false);
    }

    private static bool ReturnsTaskOfInt32(IMethodSymbol method, INamedTypeSymbol? taskSymbol)
    {
        if (method.ReturnType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (taskSymbol is null)
        {
            return string.Equals(namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Threading.Tasks.Task<int>", StringComparison.Ordinal);
        }

        if (!SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, taskSymbol))
        {
            return false;
        }

        if (namedType.TypeArguments.Length != 1)
        {
            return false;
        }

        return namedType.TypeArguments[0].SpecialType == SpecialType.System_Int32;
    }

    private static CliOptionDefinition? ToOptionDefinition(IPropertySymbol propertySymbol, INamedTypeSymbol optionAttributeSymbol)
    {
        if (propertySymbol.IsStatic)
        {
            return null;
        }

        var attribute = propertySymbol.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, optionAttributeSymbol));

        if (attribute is null)
        {
            return null;
        }

        var aliases = new List<string>();
        if (attribute.ConstructorArguments.Length == 1)
        {
            var aliasArgument = attribute.ConstructorArguments[0];
            if (aliasArgument.Kind == TypedConstantKind.Array)
            {
                foreach (var aliasConstant in aliasArgument.Values)
                {
                    var alias = GetString(aliasConstant);
                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        aliases.Add(alias);
                    }
                }
            }
        }

        if (aliases.Count == 0)
        {
            aliases.Add(ToAlias(propertySymbol.Name));
        }

        var description = GetStringArgument(attribute, "Description");
        var required = GetBoolArgument(attribute, "Required");
        var hidden = GetBoolArgument(attribute, "Hidden");

        var hasDefaultValue = TryGetNamedArgument(attribute, "DefaultValue", out var defaultConstant) && defaultConstant.Value is not null;
        var defaultValueExpression = hasDefaultValue ? GetLiteral(defaultConstant) : null;

        return new CliOptionDefinition
        {
            PropertyName = propertySymbol.Name,
            PropertyType = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Aliases = aliases,
            Description = description,
            Required = required,
            Hidden = hidden,
            DefaultValueExpression = defaultValueExpression,
            HasDefaultValue = hasDefaultValue,
        };
    }

    private static CliArgumentDefinition? ToArgumentDefinition(IPropertySymbol propertySymbol, INamedTypeSymbol argumentAttributeSymbol)
    {
        if (propertySymbol.IsStatic)
        {
            return null;
        }

        var attribute = propertySymbol.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, argumentAttributeSymbol));

        if (attribute is null)
        {
            return null;
        }

        if (attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var positionConstant = attribute.ConstructorArguments[0];
        if (positionConstant.Kind != TypedConstantKind.Primitive || positionConstant.Value is not int position)
        {
            return null;
        }

        var name = GetStringArgument(attribute, "Name") ?? propertySymbol.Name.ToCamelCase();
        var description = GetStringArgument(attribute, "Description");
        var required = GetBoolArgument(attribute, "Required", defaultValue: true);

        return new CliArgumentDefinition
        {
            PropertyName = propertySymbol.Name,
            PropertyType = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Position = position,
            ArgumentName = name,
            Description = description,
            Required = required,
        };
    }

    private static string BuildDeclaration(INamedTypeSymbol classSymbol)
    {
        var parts = new List<string>();
        var accessibility = classSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "protected internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(accessibility))
        {
            parts.Add(accessibility);
        }

        if (classSymbol.IsStatic)
        {
            parts.Add("static");
        }
        else
        {
            if (classSymbol.IsAbstract && !classSymbol.IsSealed)
            {
                parts.Add("abstract");
            }

            if (classSymbol.IsSealed && !classSymbol.IsAbstract)
            {
                parts.Add("sealed");
            }
        }

        parts.Add("partial");
        parts.Add("class");
        parts.Add(classSymbol.Name);

        return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool TryGetNamedArgument(AttributeData attribute, string name, out TypedConstant value)
    {
        foreach (var kvp in attribute.NamedArguments)
        {
            if (kvp.Key == name)
            {
                value = kvp.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringArgument(AttributeData attribute, string name)
    {
        return TryGetNamedArgument(attribute, name, out var constant) ? GetString(constant) : null;
    }

    private static bool GetBoolArgument(AttributeData attribute, string name, bool defaultValue = false)
    {
        if (TryGetNamedArgument(attribute, name, out var constant) && constant.Value is bool boolValue)
        {
            return boolValue;
        }

        return defaultValue;
    }

    private static string? GetConstructorArgumentString(AttributeData attribute, int index)
    {
        if (index < attribute.ConstructorArguments.Length)
        {
            return GetString(attribute.ConstructorArguments[index]);
        }

        return null;
    }

    private static string? GetString(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Primitive && constant.Value is string value)
        {
            return value;
        }

        return null;
    }

    private static string GetLiteral(TypedConstant constant)
    {
        return constant.ToCSharpString();
    }

    private static string ToAlias(string propertyName)
    {
        return "--" + propertyName.ToKebabCase();
    }

    // Pseudocode plan:
    // 1. Locate where the source generator emits the code for the Command objects (not shown in the provided code, but likely in a producer or emitter class using CliCommandDefinition).
    // 2. For each CliCommandDefinition with HasHandler == true, generate code to call SetAction on the generated Command object, wiring it to the Execute or ExecuteAsync method.
    // 3. The SetAction lambda should instantiate the command class, map arguments/options, and call the handler method, passing the CancellationToken if needed.
    // 4. Ensure the generated code matches the .NET Standard 2.0 and C# 13.0 constraints.

    // Since the code that emits the source for the Command objects is not shown, here's a representative method to add to your code emitter (e.g., in a class that generates the command registration code):
    private void EmitSetAction(CliCommandDefinition command, IndentedTextWriter sb)
    {
        if (!command.HasHandler || string.IsNullOrEmpty(command.HandlerMethodName))
            return;

        var commandVar = command.TypeName.ToCamelCase() + "Command";
        var instanceVar = command.TypeName.ToCamelCase() + "Instance";
        var handlerMethod = command.HandlerMethodName;
        var usesCancellationToken = command.HandlerUsesCancellationToken;

        sb.WriteLine($"{commandVar}.SetAction(async context =>");
        sb.WriteLine("{");
        sb.Indent++;

        // Instantiate the command class
        sb.WriteLine($"var {instanceVar} = new {command.TypeName}();");

        // Map options
        foreach (var option in command.Options)
        {
            var property = option.PropertyName;
            var type = option.PropertyType;
            var alias = option.Aliases.First();
            sb.WriteLine($"{instanceVar}.{property} = context.ParseResult.GetValueForOption<{type}>(\"{alias}\");");
        }

        // Map arguments
        foreach (var argument in command.Arguments)
        {
            var property = argument.PropertyName;
            var type = argument.PropertyType;
            var name = argument.ArgumentName;
            sb.WriteLine($"{instanceVar}.{property} = context.ParseResult.GetValueForArgument<{type}>(\"{name}\");");
        }

        // Call the handler
        if (usesCancellationToken)
        {
            sb.WriteLine($"return await {instanceVar}.{handlerMethod}(context.GetCancellationToken());");
        }
        else
        {
            sb.WriteLine($"return await {instanceVar}.{handlerMethod}();");
        }

        sb.Indent--;
        sb.WriteLine("});");
    }


    // Usage: In your code generation logic, after creating the Command object, call EmitSetAction for each command definition.

    // Note: You may need to adjust the context variable and method signatures to match your actual codebase.
}
