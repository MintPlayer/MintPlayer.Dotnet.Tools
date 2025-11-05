using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MintPlayer.SourceGenerators.Cli.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Cli.Generators;

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
                writer.Write("namespace ");
                writer.Write(group.Key);
                writer.WriteLine();
                writer.WriteLine("{");
                writer.Indent++;

                foreach (var tree in group)
                {
                    WriteCommandTree(writer, tree);
                    writer.WriteLine();
                }

                writer.Indent--;
                writer.WriteLine("}");
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
        writer.WriteLine(node.Command.Declaration);
        writer.WriteLine("{");
        writer.Indent++;

        WriteRegisterMethod(writer, node);
        writer.WriteLine();
        WriteBuildMethod(writer, node, isRoot);

        if (node.Children.Length > 0)
        {
            writer.WriteLine();
            foreach (var child in node.Children)
            {
                WriteCommandNode(writer, child, isRoot: false);
                writer.WriteLine();
            }
        }

        writer.Indent--;
        writer.WriteLine("}");
    }

    private void WriteRegisterMethod(IndentedTextWriter writer, CliCommandTree node)
    {
        writer.WriteLine("internal static void RegisterCliCommandTree(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<{node.Command.FullyQualifiedName}>(services);");
        foreach (var child in node.Children)
        {
            writer.WriteLine($"{child.Command.FullyQualifiedName}.RegisterCliCommandTree(services);");
        }
        writer.Indent--;
        writer.WriteLine("}");
    }

    private void WriteBuildMethod(IndentedTextWriter writer, CliCommandTree node, bool isRoot)
    {
        if (isRoot)
        {
            writer.WriteLine("internal static global::System.CommandLine.RootCommand BuildCliRootCommand(global::System.IServiceProvider serviceProvider)");
        }
        else
        {
            writer.WriteLine("internal static global::System.CommandLine.Command BuildCliCommand(global::System.IServiceProvider serviceProvider)");
        }

        writer.WriteLine("{");
        writer.Indent++;

        var command = node.Command;
        if (isRoot)
        {
            var descriptionLiteral = ToNullableStringLiteral(command.Description);
            writer.WriteLine($"var command = new global::System.CommandLine.RootCommand({descriptionLiteral});");
            if (!string.IsNullOrWhiteSpace(command.CommandName))
            {
                writer.WriteLine($"command.Name = {ToStringLiteral(command.CommandName!)};");
            }
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
            writer.WriteLine($"command.AddCommand({child.Command.FullyQualifiedName}.BuildCliCommand(serviceProvider));");
        }

        if (command.HasHandler)
        {
            writer.WriteLine();
            WriteHandler(writer, command, optionBindings, argumentBindings);
        }

        writer.WriteLine();
        writer.WriteLine("return command;");

        writer.Indent--;
        writer.WriteLine("}");
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
            var aliasExpression = $"new[] {{ {string.Join(", ", option.Aliases.Select(ToStringLiteral))} }}";
            var descriptionLiteral = ToNullableStringLiteral(option.Description);
            writer.WriteLine($"var {variableName} = new global::System.CommandLine.Option<{option.PropertyType}>({aliasExpression}, description: {descriptionLiteral});");
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
                writer.WriteLine($"{variableName}.SetDefaultValue({option.DefaultValueExpression});");
            }

            writer.WriteLine($"command.AddOption({variableName});");
            bindings.Add(new OptionBinding(option.PropertyName, variableName));
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
            writer.WriteLine($"var {variableName} = new global::System.CommandLine.Argument<{argument.PropertyType}>(name: {nameLiteral}, description: {descriptionLiteral});");
            var arity = argument.Required
                ? "global::System.CommandLine.ArgumentArity.ExactlyOne"
                : "global::System.CommandLine.ArgumentArity.ZeroOrOne";
            writer.WriteLine($"{variableName}.Arity = {arity};");
            writer.WriteLine($"command.AddArgument({variableName});");
            bindings.Add(new ArgumentBinding(argument.PropertyName, variableName));
            writer.WriteLine();
        }

        return bindings;
    }

    private void WriteHandler(IndentedTextWriter writer, CliCommandDefinition command, IReadOnlyList<OptionBinding> options, IReadOnlyList<ArgumentBinding> arguments)
    {
        var isAsync = command.HandlerReturnKind is CliHandlerReturnKind.Task or CliHandlerReturnKind.TaskOfInt32 or CliHandlerReturnKind.ValueTask or CliHandlerReturnKind.ValueTaskOfInt32;
        writer.WriteLine(isAsync
            ? "command.SetHandler(async invocationContext =>"
            : "command.SetHandler(invocationContext =>");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("using var scope = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(serviceProvider);");
        writer.WriteLine($"var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{command.FullyQualifiedName}>(scope.ServiceProvider);");

        foreach (var option in options)
        {
            writer.WriteLine($"handler.{option.PropertyName} = invocationContext.ParseResult.GetValueForOption({option.VariableName});");
        }

        foreach (var argument in arguments)
        {
            writer.WriteLine($"handler.{argument.PropertyName} = invocationContext.ParseResult.GetValueForArgument({argument.VariableName});");
        }

        if (command.HandlerUsesCancellationToken)
        {
            writer.WriteLine("var cancellationToken = invocationContext.GetCancellationToken();");
        }

        var invocation = command.HandlerUsesCancellationToken
            ? $"handler.{command.HandlerMethodName}(cancellationToken)"
            : $"handler.{command.HandlerMethodName}()";

        switch (command.HandlerReturnKind)
        {
            case CliHandlerReturnKind.Int32:
                writer.WriteLine($"var exitCode = {invocation};");
                writer.WriteLine("invocationContext.ExitCode = exitCode;");
                break;
            case CliHandlerReturnKind.Task:
                writer.WriteLine($"await {invocation};");
                writer.WriteLine("invocationContext.ExitCode = 0;");
                break;
            case CliHandlerReturnKind.TaskOfInt32:
                writer.WriteLine($"var exitCodeTask = {invocation};");
                writer.WriteLine("var exitCode = await exitCodeTask;");
                writer.WriteLine("invocationContext.ExitCode = exitCode;");
                break;
            case CliHandlerReturnKind.ValueTask:
                writer.WriteLine($"await {invocation};");
                writer.WriteLine("invocationContext.ExitCode = 0;");
                break;
            case CliHandlerReturnKind.ValueTaskOfInt32:
                writer.WriteLine($"var exitCodeValueTask = {invocation};");
                writer.WriteLine("var exitCode = await exitCodeValueTask;");
                writer.WriteLine("invocationContext.ExitCode = exitCode;");
                break;
            default:
                writer.WriteLine(invocation + ";");
                writer.WriteLine("invocationContext.ExitCode = 0;");
                break;
        }

        writer.Indent--;
        writer.WriteLine("});");
    }

    private void WriteRootExtensions(IndentedTextWriter writer, CliCommandDefinition command)
    {
        var extensionClassName = command.TypeName + "CliExtensions";
        writer.WriteLine($"public static class {extensionClassName}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{command.TypeName}CommandTree(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"{command.FullyQualifiedName}.RegisterCliCommandTree(services);");
        writer.WriteLine("return services;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine($"public static global::System.CommandLine.RootCommand Build{command.TypeName}Command(this global::System.IServiceProvider serviceProvider)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"return {command.FullyQualifiedName}.BuildCliRootCommand(serviceProvider);");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine($"public static global::System.Threading.Tasks.Task<int> Invoke{command.TypeName}CommandAsync(this global::System.IServiceProvider serviceProvider, string[] args)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"var command = {command.FullyQualifiedName}.BuildCliRootCommand(serviceProvider);");
        writer.WriteLine("return command.InvokeAsync(args);");
        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
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

    private readonly record struct OptionBinding(string PropertyName, string VariableName);
    private readonly record struct ArgumentBinding(string PropertyName, string VariableName);
}
