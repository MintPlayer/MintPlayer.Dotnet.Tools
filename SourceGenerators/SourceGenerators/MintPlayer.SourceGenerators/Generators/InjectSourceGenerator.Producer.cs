using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

internal class InjectProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos;
    public InjectProducer(IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos, string rootNamespace) : base(rootNamespace, $"Inject.g.cs")
    {
        this.classInfos = classInfos;
    }
    public InjectProducer(IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos, string rootNamespace, string filename) : base(rootNamespace, filename)
    {
        this.classInfos = classInfos;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        var postConstructDiagnostics = classInfos
            .SelectMany(ci => ci.Diagnostics)
            .Select(d => d.Rule.Create(d.Location?.ToLocation(compilation), d.MessageArgs));

        var configDiagnostics = classInfos
            .SelectMany(ci => ci.ConfigDiagnostics)
            .Select(d => d.Rule.Create(d.Location?.ToLocation(compilation), d.MessageArgs));

        return postConstructDiagnostics.Concat(configDiagnostics);
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var classInfoNamespace in classInfos.GroupBy(ci => ci.PathSpec?.ContainingNamespace ?? ci.ClassNamespace ?? RootNamespace))
        {
            IDisposableWriterIndent? namespaceBlock = string.IsNullOrEmpty(classInfoNamespace.Key) ? null : writer.OpenBlock($"namespace {classInfoNamespace.Key}");

            foreach (var classInfo in classInfoNamespace)
            {
                // Determine if we need IConfiguration (for [Config] or [ConnectionString] fields)
                var needsIConfiguration = (classInfo.ConfigFields.Count > 0 || classInfo.ConnectionStringFields.Count > 0)
                                          && !classInfo.HasExplicitIConfiguration;

                // Build constructor parameters
                var constructorParams = new List<string>();

                // Add auto-injected IConfiguration if needed
                if (needsIConfiguration)
                {
                    constructorParams.Add("global::Microsoft.Extensions.Configuration.IConfiguration __configuration");
                }

                // Add [Inject] fields
                foreach (var dep in classInfo.InjectFields)
                {
                    constructorParams.Add($"{dep.Type} {dep.Name}");
                }

                // Add [Options] fields (injected as constructor params)
                foreach (var opt in classInfo.OptionsFields)
                {
                    constructorParams.Add($"{opt.Type} {opt.Name}");
                }

                // Add base dependencies
                foreach (var dep in classInfo.BaseDependencies)
                {
                    var paramStr = $"{dep.Type} {dep.Name}";
                    if (!constructorParams.Contains(paramStr))
                        constructorParams.Add(paramStr);
                }

                // Skip if no constructor params at all
                var hasConfigWork = classInfo.ConfigFields.Count > 0 || classInfo.ConnectionStringFields.Count > 0;
                if (!constructorParams.Any() && !hasConfigWork) continue;

                var baseConstructorArgs = classInfo.BaseDependencies.Select(dep => dep.Name).Distinct().ToList();

                using (writer.OpenPathSpec(classInfo.PathSpec))
                {
                    // Build class declaration with generic type parameters
                    var classDeclaration = $"partial class {classInfo.ClassName}{classInfo.GenericTypeParameters ?? string.Empty}";

                    // Handle generic constraints if present
                    if (!string.IsNullOrEmpty(classInfo.GenericConstraints))
                    {
                        writer.WriteLine(classDeclaration);
                        writer.IndentSingleLine(classInfo.GenericConstraints);
                        using (writer.OpenBlock(string.Empty))
                        {
                            WriteConstructorBody(writer, classInfo, constructorParams, baseConstructorArgs, needsIConfiguration);
                        }
                    }
                    else
                    {
                        using (writer.OpenBlock(classDeclaration))
                        {
                            WriteConstructorBody(writer, classInfo, constructorParams, baseConstructorArgs, needsIConfiguration);
                        }
                    }
                }
            }

            namespaceBlock?.Dispose();
        }
    }

    private static void WriteConstructorBody(
        IndentedTextWriter writer,
        Models.ClassWithBaseDependenciesAndInjectFields classInfo,
        List<string> constructorParams,
        List<string> baseConstructorArgs,
        bool needsIConfiguration)
    {
        writer.WriteLine($"public {classInfo.ClassName}({string.Join(", ", constructorParams)})");
        if (baseConstructorArgs.Any())
            writer.IndentSingleLine($": base({string.Join(", ", baseConstructorArgs)})");

        using (writer.OpenBlock(string.Empty))
        {
            // [Inject] field assignments
            foreach (var dep in classInfo.InjectFields)
            {
                writer.WriteLine($"this.{dep.Name} = {dep.Name};");
            }

            // [Options] field assignments
            foreach (var opt in classInfo.OptionsFields)
            {
                writer.WriteLine($"this.{opt.Name} = {opt.Name};");
            }

            // Determine the configuration variable name
            var configVar = classInfo.HasExplicitIConfiguration
                ? classInfo.ExplicitIConfigurationName!
                : "__configuration";

            // [Config] field assignments
            foreach (var cfg in classInfo.ConfigFields)
            {
                WriteConfigAssignment(writer, cfg, configVar);
            }

            // [ConnectionString] field assignments
            foreach (var connStr in classInfo.ConnectionStringFields)
            {
                WriteConnectionStringAssignment(writer, connStr, configVar);
            }

            // [PostConstruct] call
            if (!string.IsNullOrEmpty(classInfo.PostConstructMethodName))
                writer.WriteLine($"{classInfo.PostConstructMethodName}();");
        }
    }

    private static void WriteConfigAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        var fieldName = cfg.Name;
        var key = cfg.Key;

        switch (cfg.TypeCategory)
        {
            case ConfigFieldTypeCategory.String:
                WriteStringAssignment(writer, cfg, configVar);
                break;

            case ConfigFieldTypeCategory.Boolean:
                WriteParsedAssignment(writer, cfg, configVar, "bool", "bool.Parse", needsCulture: false);
                break;

            case ConfigFieldTypeCategory.Char:
                WriteCharAssignment(writer, cfg, configVar);
                break;

            case ConfigFieldTypeCategory.Numeric:
                WriteNumericAssignment(writer, cfg, configVar);
                break;

            case ConfigFieldTypeCategory.Enum:
                WriteEnumAssignment(writer, cfg, configVar);
                break;

            case ConfigFieldTypeCategory.DateTime:
                WriteParsedAssignment(writer, cfg, configVar, cfg.Type, $"{cfg.Type}.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.TimeSpan:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.TimeSpan", "global::System.TimeSpan.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.DateOnly:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.DateOnly", "global::System.DateOnly.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.TimeOnly:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.TimeOnly", "global::System.TimeOnly.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.Guid:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.Guid", "global::System.Guid.Parse", needsCulture: false);
                break;

            case ConfigFieldTypeCategory.Uri:
                WriteUriAssignment(writer, cfg, configVar);
                break;

            case ConfigFieldTypeCategory.Complex:
                WriteComplexAssignment(writer, cfg, configVar);
                break;

            case ConfigFieldTypeCategory.Collection:
                WriteCollectionAssignment(writer, cfg, configVar);
                break;
        }
    }

    private static void WriteStringAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        // Required = non-nullable without default value
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] ?? throw new global::System.InvalidOperationException(\"Configuration key '{cfg.Key}' is required but was not found.\");");
        }
        else if (cfg.HasDefaultValue)
        {
            var defaultStr = cfg.DefaultValue is string s ? $"\"{EscapeString(s)}\"" : "null";
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] ?? {defaultStr};");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"];");
        }
    }

    private static void WriteCharAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        var tempVar = $"__{cfg.Name}Value";
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = ({configVar}[\"{cfg.Key}\"] ?? throw new global::System.InvalidOperationException(\"Configuration key '{cfg.Key}' is required but was not found.\"))[0];");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} && {tempVar}.Length > 0 ? {tempVar}[0] : null;");
        }
        else if (cfg.HasDefaultValue)
        {
            var defaultChar = cfg.DefaultValue is char c ? $"'{c}'" : $"'{cfg.DefaultValue}'";
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} && {tempVar}.Length > 0 ? {tempVar}[0] : {defaultChar};");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} && {tempVar}.Length > 0 ? {tempVar}[0] : default;");
        }
    }

    private static void WriteParsedAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar, string typeName, string parseMethod, bool needsCulture)
    {
        var tempVar = $"__{cfg.Name}Value";
        var cultureArg = needsCulture ? ", global::System.Globalization.CultureInfo.InvariantCulture" : "";
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = {parseMethod}({configVar}[\"{cfg.Key}\"] ?? throw new global::System.InvalidOperationException(\"Configuration key '{cfg.Key}' is required but was not found.\"){cultureArg});");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? {parseMethod}({tempVar}{cultureArg}) : null;");
        }
        else if (cfg.HasDefaultValue)
        {
            var defaultVal = FormatDefaultValue(cfg.DefaultValue, cfg.TypeCategory);
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? {parseMethod}({tempVar}{cultureArg}) : {defaultVal};");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? {parseMethod}({tempVar}{cultureArg}) : default;");
        }
    }

    private static void WriteNumericAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        // Determine the correct parse method based on type
        var parseMethod = GetNumericParseMethod(cfg.Type);
        WriteParsedAssignment(writer, cfg, configVar, cfg.Type, parseMethod, needsCulture: IsFloatingPoint(cfg.Type));
    }

    private static void WriteEnumAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        var enumType = cfg.ElementOrUnderlyingType ?? cfg.Type;
        var tempVar = $"__{cfg.Name}Value";
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = global::System.Enum.Parse<{enumType}>({configVar}[\"{cfg.Key}\"] ?? throw new global::System.InvalidOperationException(\"Configuration key '{cfg.Key}' is required but was not found.\"));");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? global::System.Enum.Parse<{enumType}>({tempVar}) : null;");
        }
        else if (cfg.HasDefaultValue)
        {
            // For enums, default value might be the enum value or its underlying int
            var defaultVal = cfg.DefaultValue is int i ? $"({enumType}){i}" : $"{enumType}.{cfg.DefaultValue}";
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? global::System.Enum.Parse<{enumType}>({tempVar}) : {defaultVal};");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? global::System.Enum.Parse<{enumType}>({tempVar}) : default;");
        }
    }

    private static void WriteUriAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        var tempVar = $"__{cfg.Name}Value";
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = new global::System.Uri({configVar}[\"{cfg.Key}\"] ?? throw new global::System.InvalidOperationException(\"Configuration key '{cfg.Key}' is required but was not found.\"));");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? new global::System.Uri({tempVar}) : null;");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? new global::System.Uri({tempVar}) : null!;");
        }
    }

    private static void WriteComplexAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        // Use fully qualified extension method call since generated code has no using statements
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = global::Microsoft.Extensions.Configuration.ConfigurationBinder.Get<{cfg.Type}>({configVar}.GetSection(\"{cfg.Key}\")) ?? throw new global::System.InvalidOperationException(\"Configuration section '{cfg.Key}' is required but could not be bound to type '{cfg.Type}'.\");");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = global::Microsoft.Extensions.Configuration.ConfigurationBinder.Get<{cfg.Type}>({configVar}.GetSection(\"{cfg.Key}\"));");
        }
    }

    private static void WriteCollectionAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        // Use fully qualified extension method call since generated code has no using statements
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = global::Microsoft.Extensions.Configuration.ConfigurationBinder.Get<{cfg.Type}>({configVar}.GetSection(\"{cfg.Key}\")) ?? throw new global::System.InvalidOperationException(\"Configuration section '{cfg.Key}' is required but was not found.\");");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = global::Microsoft.Extensions.Configuration.ConfigurationBinder.Get<{cfg.Type}>({configVar}.GetSection(\"{cfg.Key}\"));");
        }
    }

    private static void WriteConnectionStringAssignment(IndentedTextWriter writer, ConnectionStringField connStr, string configVar)
    {
        // Use fully qualified extension method call since generated code has no using statements
        // Required is inferred from nullability: non-nullable = required
        var isRequired = !connStr.IsNullable;

        if (isRequired)
        {
            writer.WriteLine($"this.{connStr.FieldName} = global::Microsoft.Extensions.Configuration.ConfigurationExtensions.GetConnectionString({configVar}, \"{connStr.Name}\") ?? throw new global::System.InvalidOperationException(\"Connection string '{connStr.Name}' is required but was not found.\");");
        }
        else
        {
            writer.WriteLine($"this.{connStr.FieldName} = global::Microsoft.Extensions.Configuration.ConfigurationExtensions.GetConnectionString({configVar}, \"{connStr.Name}\");");
        }
    }

    private static string GetNumericParseMethod(string type)
    {
        // Strip global:: prefix and nullable
        var cleanType = type.Replace("global::", "").TrimEnd('?');

        return cleanType switch
        {
            "System.Byte" or "byte" => "byte.Parse",
            "System.SByte" or "sbyte" => "sbyte.Parse",
            "System.Int16" or "short" => "short.Parse",
            "System.UInt16" or "ushort" => "ushort.Parse",
            "System.Int32" or "int" => "int.Parse",
            "System.UInt32" or "uint" => "uint.Parse",
            "System.Int64" or "long" => "long.Parse",
            "System.UInt64" or "ulong" => "ulong.Parse",
            "System.Single" or "float" => "float.Parse",
            "System.Double" or "double" => "double.Parse",
            "System.Decimal" or "decimal" => "decimal.Parse",
            _ => "int.Parse"
        };
    }

    private static bool IsFloatingPoint(string type)
    {
        var cleanType = type.Replace("global::", "").TrimEnd('?');
        return cleanType is "System.Single" or "float" or "System.Double" or "double" or "System.Decimal" or "decimal";
    }

    private static string FormatDefaultValue(object? value, ConfigFieldTypeCategory category)
    {
        if (value == null) return "null";

        return value switch
        {
            bool b => b ? "true" : "false",
            char c => $"'{c}'",
            string s => $"\"{EscapeString(s)}\"",
            float f => $"{f}f",
            double d => $"{d}d",
            decimal m => $"{m}m",
            _ => value.ToString() ?? "default"
        };
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
