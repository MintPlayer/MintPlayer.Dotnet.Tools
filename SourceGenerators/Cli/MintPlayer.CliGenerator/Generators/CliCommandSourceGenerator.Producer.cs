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

        var groups = commandTrees
            .GroupBy(tree => ResolveNamespace(tree.Command.Namespace))
            .OrderBy(group => group.Key ?? string.Empty);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (group.Key is null)
            {
                foreach (var tree in group)
                {
                    WriteCommandTree(writer, tree);
                    writer.WriteLine();
                }
            }
            else
            {
                using (writer.OpenBlock($"namespace {group.Key}"))
                {
                    foreach (var tree in group)
                    {
                        WriteCommandTree(writer, tree);
                        writer.WriteLine();
                    }
                }
                writer.WriteLine();
            }
        }
    }

    private string? ResolveNamespace(string? declaredNamespace)
    {
        if (!string.IsNullOrWhiteSpace(declaredNamespace))
        {
            return declaredNamespace;
        }

        if (!string.IsNullOrWhiteSpace(RootNamespace))
        {
            return RootNamespace;
        }

        return null;
    }

    private void WriteCommandTree(IndentedTextWriter writer, CliCommandTree tree)
    {
        WriteCommandNode(writer, tree, isRoot: true);
        writer.WriteLine();
        WriteRootExtensions(writer, tree.Command);
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
                var descriptionLiteral = ToNullableStringLiteral(command.Description);
                writer.WriteLine($"var command = new global::System.CommandLine.RootCommand({descriptionLiteral});");
            }
            else
            {
                var nameLiteral = ToStringLiteral(command.CommandName ?? command.TypeName.ToLowerInvariant());
                var descriptionLiteral = ToNullableStringLiteral(command.Description);
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
            var optionNameLiteral = ToStringLiteral(GetOptionName(option));
            var aliasExpression = option.Aliases.Count > 0
                ? $"new[] {{ {string.Join(", ", option.Aliases.Select(ToStringLiteral))} }}"
                : "global::System.Array.Empty<string>()";
            writer.WriteLine($"var {variableName} = new global::System.CommandLine.Option<{option.PropertyType}>({optionNameLiteral}, {aliasExpression});");
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                var descriptionLiteral = ToStringLiteral(option.Description!);
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
            var nameLiteral = ToStringLiteral(argument.ArgumentName);
            var descriptionLiteral = ToNullableStringLiteral(argument.Description);
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
            using (writer.OpenBlock($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{command.TypeName}Tree(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)"))
            {
                writer.WriteLine($"{command.FullyQualifiedName}.RegisterCliCommandTree(services);");
                writer.WriteLine("return services;");
            }
            writer.WriteLine();

            using (writer.OpenBlock($"public static global::System.CommandLine.RootCommand Build{command.TypeName}Command(this global::System.IServiceProvider serviceProvider)"))
            {
                writer.WriteLine($"return {command.FullyQualifiedName}.BuildCliRootCommand(serviceProvider);");
            }
            writer.WriteLine();

            using (writer.OpenBlock($"public static global::System.Threading.Tasks.Task<int> Invoke{command.TypeName}Async(this global::System.IServiceProvider serviceProvider, string[] args)"))
            {
                writer.WriteLine($"var command = {command.FullyQualifiedName}.BuildCliRootCommand(serviceProvider);");
                writer.WriteLine("var parsedCommand = command.Parse(args);");
                writer.WriteLine("return parsedCommand.InvokeAsync();");
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

    private static string ToNullableStringLiteral(string? value)
    {
        return value is null
            ? "null"
            : ToStringLiteral(value);
    }

    private static string ToStringLiteral(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
        return $"\"{escaped}\"";
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
