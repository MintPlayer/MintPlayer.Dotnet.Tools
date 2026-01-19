using MintPlayer.CliGenerator.Extensions;
using MintPlayer.CliGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace MintPlayer.CliGenerator.Generators;

internal sealed class CliCommandProducer : Producer
{
    private readonly ImmutableArray<CliCommandTree> commandTrees;

    public CliCommandProducer(ImmutableArray<CliCommandTree> commandTrees, string rootNamespace)
        : base(rootNamespace, commandTrees.IsDefaultOrEmpty || commandTrees.Length == 0 ? string.Empty : "CliCommands.g.cs")
    {
        this.commandTrees = commandTrees;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        if (commandTrees.IsDefaultOrEmpty || commandTrees.Length == 0)
        {
            return;
        }

        writer.WriteLine(Header);
        writer.WriteLine();

        // Group commands by their ACTUAL declared namespace for partial class generation
        // Important: Use the symbol's namespace directly, NOT the fallback to RootNamespace
        // A partial class MUST be in the same namespace as the original declaration
        var groups = commandTrees
            .GroupBy(tree => string.IsNullOrEmpty(tree.Command.Namespace) ? null : tree.Command.Namespace)
            .OrderBy(group => group.Key ?? string.Empty);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (group.Key is null)
            {
                // Global namespace - no namespace block
                foreach (var tree in group)
                {
                    WriteCommandNode(writer, tree, isRoot: true);
                    writer.WriteLine();
                }
            }
            else
            {
                using (writer.OpenBlock($"namespace {group.Key}"))
                {
                    foreach (var tree in group)
                    {
                        WriteCommandNode(writer, tree, isRoot: true);
                        writer.WriteLine();
                    }
                }
                writer.WriteLine();
            }
        }

        // Write all extension methods in RootNamespace for consistency
        WriteAllExtensions(writer, commandTrees, cancellationToken);
    }

    private void WriteAllExtensions(IndentedTextWriter writer, ImmutableArray<CliCommandTree> trees, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(RootNamespace))
        {
            using (writer.OpenBlock($"namespace {RootNamespace}"))
            {
                foreach (var tree in trees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteRootExtensions(writer, tree.Command);
                    writer.WriteLine();
                }
            }
        }
        else
        {
            // No root namespace - write extensions at global scope
            foreach (var tree in trees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteRootExtensions(writer, tree.Command);
                writer.WriteLine();
            }
        }
    }

    private void WriteCommandNode(IndentedTextWriter writer, CliCommandTree node, bool isRoot)
    {
        using (writer.OpenPathSpec(node.Command.PathSpec))
        {
            using (writer.OpenBlock(node.Command.Declaration))
            {
                WriteRegisterMethod(writer, node);
                writer.WriteLine();
                WriteBuildMethod(writer, node, isRoot);
            }
        }

        foreach (var child in node.Children)
        {
            writer.WriteLine();
            WriteCommandNode(writer, child, isRoot: false);
        }
    }

    private void WriteRegisterMethod(IndentedTextWriter writer, CliCommandTree node)
    {
        using (writer.OpenBlock("internal static void RegisterCliCommandTree(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)"))
        {
            writer.WriteLine($"global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<{node.Command.FullyQualifiedName}>(services);");
            foreach (var child in node.Children)
            {
                writer.WriteLine($"{child.Command.FullyQualifiedName}.RegisterCliCommandTree(services);");
            }
        }
    }

    private void WriteBuildMethod(IndentedTextWriter writer, CliCommandTree node, bool isRoot)
    {
        var methodSignature = isRoot
            ? "internal static global::System.CommandLine.RootCommand BuildCliRootCommand(global::System.IServiceProvider serviceProvider)"
            : "internal static global::System.CommandLine.Command BuildCliCommand(global::System.IServiceProvider serviceProvider)";

        using (writer.OpenBlock(methodSignature))
        {
            var command = node.Command;
            if (isRoot)
            {
                var descriptionLiteral = command.Description.ToStringLiteral();
                writer.WriteLine($"var command = new global::System.CommandLine.RootCommand({descriptionLiteral});");
            }
            else
            {
                var nameLiteral = (command.CommandName ?? command.TypeName.ToLowerInvariant()).ToStringLiteral();
                var descriptionLiteral = command.Description.ToStringLiteral();
                writer.WriteLine($"var command = new global::System.CommandLine.Command({nameLiteral}, description: {descriptionLiteral});");
            }

            var optionBindings = WriteOptionDeclarations(writer, command);
            var argumentBindings = WriteArgumentDeclarations(writer, command);

            foreach (var child in node.Children)
            {
                writer.WriteLine($"command.Add({child.Command.FullyQualifiedName}.BuildCliCommand(serviceProvider));");
            }

            if (command.HasHandler)
            {
                WriteHandler(writer, command, optionBindings, argumentBindings);
            }
            else
            {
                using (writer.OpenBlock("command.SetAction(async parseResult =>"))
                {
                    writer.WriteLine("using var scope = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(serviceProvider);");
                    writer.WriteLine($"var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{command.FullyQualifiedName}>(scope.ServiceProvider);");

                    foreach (var option in optionBindings)
                    {
                        var method = option.Required ? "GetRequiredValue" : "GetValue";
                        writer.WriteLine($"handler.{option.PropertyName} = parseResult.{method}({option.VariableName});");
                    }

                    foreach (var argument in argumentBindings)
                    {
                        var method = argument.Required ? "GetRequiredValue" : "GetValue";
                        writer.WriteLine($"handler.{argument.PropertyName} = parseResult.{method}({argument.VariableName});");
                    }

                    writer.WriteLine("return await handler.Execute(global::System.Threading.CancellationToken.None);");
                }
                writer.WriteLine(");");
            }

            writer.WriteLine();
            writer.WriteLine("return command;");
        }
    }

    private IReadOnlyList<OptionBinding> WriteOptionDeclarations(IndentedTextWriter writer, CliCommandDefinition command)
    {
        var bindings = new List<OptionBinding>();
        if (command.Options.IsDefaultOrEmpty || command.Options.Length == 0)
        {
            return bindings;
        }

        for (var i = 0; i < command.Options.Length; i++)
        {
            var option = command.Options[i];
            var variableName = $"option{NormalizeIdentifier(option.PropertyName)}";
            var optionNameLiteral = GetOptionName(option).ToStringLiteral();
            var aliasExpression = option.Aliases.Count > 0
                ? $"new[] {{ {string.Join(", ", option.Aliases.Select(a => a.ToStringLiteral()))} }}"
                : "global::System.Array.Empty<string>()";
            writer.WriteLine($"var {variableName} = new global::System.CommandLine.Option<{option.PropertyType}>({optionNameLiteral}, {aliasExpression});");
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                var descriptionLiteral = option.Description!.ToStringLiteral();
                writer.WriteLine($"{variableName}.Description = {descriptionLiteral};");
            }
            if (option.Required)
            {
                writer.WriteLine($"{variableName}.IsRequired = true;");
            }

            if (option.Hidden)
            {
                writer.WriteLine($"{variableName}.IsHidden = true;");
            }

            if (option.HasDefaultValue && option.DefaultValueExpression is not null)
            {
                writer.WriteLine($"{variableName}.DefaultValueFactory = static _ => {option.DefaultValueExpression};");
            }

            writer.WriteLine($"command.Add({variableName});");
            bindings.Add(new OptionBinding(option.PropertyName, variableName, option.Required));
            writer.WriteLine();
        }

        return bindings;
    }

    private IReadOnlyList<ArgumentBinding> WriteArgumentDeclarations(IndentedTextWriter writer, CliCommandDefinition command)
    {
        var bindings = new List<ArgumentBinding>();
        if (command.Arguments.IsDefaultOrEmpty || command.Arguments.Length == 0)
        {
            return bindings;
        }

        for (var i = 0; i < command.Arguments.Length; i++)
        {
            var argument = command.Arguments[i];
            var variableName = $"argument{NormalizeIdentifier(argument.PropertyName)}";
            var nameLiteral = argument.ArgumentName.ToStringLiteral();
            var descriptionLiteral = argument.Description.ToStringLiteral();
            writer.WriteLine($"var {variableName} = new global::System.CommandLine.Argument<{argument.PropertyType}>(name: {nameLiteral});");
            if (!string.IsNullOrWhiteSpace(argument.Description))
            {
                writer.WriteLine($"{variableName}.Description = {descriptionLiteral};");
            }
            var arity = argument.Required
                ? "global::System.CommandLine.ArgumentArity.ExactlyOne"
                : "global::System.CommandLine.ArgumentArity.ZeroOrOne";
            writer.WriteLine($"{variableName}.Arity = {arity};");
            writer.WriteLine($"command.Add({variableName});");
            bindings.Add(new ArgumentBinding(argument.PropertyName, variableName, argument.Required));
            writer.WriteLine();
        }

        return bindings;
    }

    private void WriteHandler(IndentedTextWriter writer, CliCommandDefinition command, IReadOnlyList<OptionBinding> options, IReadOnlyList<ArgumentBinding> arguments)
    {
        using (writer.OpenBlock("command.SetAction(async invocationContext =>"))
        {
            writer.WriteLine("using var scope = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(serviceProvider);");
            writer.WriteLine($"var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{command.FullyQualifiedName}>(scope.ServiceProvider);");

            foreach (var option in options)
            {
                var method = option.Required ? "GetRequiredValue" : "GetValue";
                writer.WriteLine($"handler.{option.PropertyName} = invocationContext.ParseResult.{method}({option.VariableName});");
            }

            foreach (var argument in arguments)
            {
                var method = argument.Required ? "GetRequiredValue" : "GetValue";
                writer.WriteLine($"handler.{argument.PropertyName} = invocationContext.ParseResult.{method}({argument.VariableName});");
            }

            if (command.HandlerUsesCancellationToken)
            {
                writer.WriteLine("var cancellationToken = invocationContext.GetCancellationToken();");
            }

            var handlerMethodName = command.HandlerMethodName ?? "Execute";
            var invocation = command.HandlerUsesCancellationToken
                ? $"handler.{handlerMethodName}(cancellationToken)"
                : $"handler.{handlerMethodName}()";

            writer.WriteLine($"var exitCode = await {invocation};");
            writer.WriteLine("invocationContext.ExitCode = exitCode;");
        }
        writer.WriteLine(");");
    }

    private void WriteRootExtensions(IndentedTextWriter writer, CliCommandDefinition command)
    {
        using (writer.OpenBlock($"public static class {command.TypeName}CliExtensions"))
        {
            using (writer.OpenBlock($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{command.TypeName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)"))
            {
                writer.WriteLine($"{command.FullyQualifiedName}.RegisterCliCommandTree(services);");
                writer.WriteLine("return services;");
            }
            writer.WriteLine();

            using (writer.OpenBlock($"public static global::System.CommandLine.RootCommand Build{command.TypeName}(this global::System.IServiceProvider serviceProvider)"))
            {
                writer.WriteLine($"return {command.FullyQualifiedName}.BuildCliRootCommand(serviceProvider);");
            }
            writer.WriteLine();

            using (writer.OpenBlock($"public static async global::System.Threading.Tasks.Task<int> Invoke{command.TypeName}Async(this global::Microsoft.Extensions.Hosting.IHost host, string[] args)"))
            {
                writer.WriteLine($"var command = {command.FullyQualifiedName}.BuildCliRootCommand(host.Services);");
                writer.WriteLine("var parsedCommand = command.Parse(args);");
                writer.WriteLine("if (parsedCommand.Errors.Count > 0)"); 
                writer.IndentSingleLine("throw new global::MintPlayer.CliGenerator.Attributes.ParseCommandException(parsedCommand.Tokens.Select(t => t.Value), parsedCommand.Errors);");
                writer.WriteLine("var result = await parsedCommand.InvokeAsync();");
                writer.WriteLine("return result;");
            }
        }
    }

    private static string NormalizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(name.Length);
        builder.Append(char.ToLowerInvariant(name[0]));
        for (var i = 1; i < name.Length; i++)
        {
            var character = name[i];
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string GetOptionName(CliOptionDefinition option)
    {
        foreach (var alias in option.Aliases)
        {
            if (alias.StartsWith("--", StringComparison.Ordinal) && alias.Length > 2)
            {
                return alias.TrimStart('-');
            }
        }

        foreach (var alias in option.Aliases)
        {
            var trimmed = alias.TrimStart('-');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return option.PropertyName.ToKebabCase();
    }

    private readonly struct OptionBinding
    {
        public OptionBinding(string propertyName, string variableName, bool required)
        {
            PropertyName = propertyName;
            VariableName = variableName;
            Required = required;
        }

        public string PropertyName { get; }
        public string VariableName { get; }
        public bool Required { get; }
    }

    private readonly struct ArgumentBinding
    {
        public ArgumentBinding(string propertyName, string variableName, bool required)
        {
            PropertyName = propertyName;
            VariableName = variableName;
            Required = required;
        }

        public string PropertyName { get; }
        public string VariableName { get; }
        public bool Required { get; }
    }
}
