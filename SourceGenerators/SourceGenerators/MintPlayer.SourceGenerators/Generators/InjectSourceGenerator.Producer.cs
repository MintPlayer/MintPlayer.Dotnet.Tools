using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

internal class InjectProducer : Producer
{
    private readonly IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos;
    public InjectProducer(IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos, string rootNamespace) : base(rootNamespace, $"Inject.g.cs")
    {
        this.classInfos = classInfos;
    }

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var classInfoNamespace in classInfos.GroupBy(ci => ci.PathSpec?.ContainingNamespace ?? string.Empty))
        {
            using (writer.OpenNamespace(classInfoNamespace.Key))
            {
                foreach (var classInfo in classInfoNamespace)
                {
                    // The path spec re-declares every containing type and the type itself, with the keyword the
                    // user wrote (class, record, struct, record struct), so every kind gets its constructor.
                    using (writer.OpenPathSpec(classInfo.PathSpec, includeTarget: true))
                    {
                        WriteConstructor(writer, classInfo);
                    }
                }
            }
        }
    }

    private static void WriteConstructor(IndentedTextWriter writer, Models.ClassWithBaseDependenciesAndInjectFields classInfo)
    {
        // A cref to a generic type needs its type parameters in braces (Foo{T}), so a generic type is named in <c>
        // instead of risking an unresolvable cref (CS1574).
        var typeReference = classInfo.IsGeneric
            ? $"<c>{classInfo.ClassName}</c>"
            : $"<see cref=\"{classInfo.ClassName}\"/>";
        var parameters = classInfo.Parameters.Select(p => p.IsNullable ? $"{p.Type} {p.Name} = default" : $"{p.Type} {p.Name}");

        writer.WriteLine($"/// <summary>Initializes a new instance of {typeReference} with its injected dependencies.</summary>");
        writer.WriteLine($"{classInfo.ConstructorAccessibility} {classInfo.ClassName}({string.Join(", ", parameters)})");
        if (classInfo.BaseConstructorArguments.Any())
            writer.IndentSingleLine($": base({string.Join(", ", classInfo.BaseConstructorArguments)})");
        else if (classInfo.IsValueType)
            // Every field of a struct must be assigned before the constructor returns (CS0171 before C# 11).
            writer.IndentSingleLine(": this()");

        using (writer.OpenBlock(string.Empty))
        {
            // [Inject] and [Options] member assignments
            foreach (var assignment in classInfo.Assignments)
            {
                writer.WriteLine($"this.{assignment.Member} = {assignment.Parameter};");
            }

            var configVar = classInfo.ConfigurationParameterName ?? "__configuration";

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
                WriteParsedAssignment(writer, cfg, configVar, "bool.Parse", needsCulture: false);
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

            // The only category whose parse target is not a fixed type name, because it covers both
            // DateTime and DateTimeOffset and has to keep them apart. cfg.Type is the DECLARED type
            // though, so for a `DateTime?` field it is "global::System.DateTime?" and the emitted
            // call was "global::System.DateTime?.Parse(...)" — CS0119 in the consumer's build.
            // Nullable Guid, TimeSpan and the rest were unaffected only because they pass a
            // hardcoded non-nullable name.
            case ConfigFieldTypeCategory.DateTime:
                var dateTimeType = NonNullable(cfg.Type);
                WriteParsedAssignment(writer, cfg, configVar, $"{dateTimeType}.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.TimeSpan:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.TimeSpan.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.DateOnly:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.DateOnly.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.TimeOnly:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.TimeOnly.Parse", needsCulture: true);
                break;

            case ConfigFieldTypeCategory.Guid:
                WriteParsedAssignment(writer, cfg, configVar, "global::System.Guid.Parse", needsCulture: false);
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
        else if (cfg.HasDefaultValue)
        {
            // CONFIG005 admits only a char (or null on a char?).
            var defaultChar = FormatDefaultValue(cfg.DefaultValue, cfg.TypeCategory);
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} && {tempVar}.Length > 0 ? {tempVar}[0] : {defaultChar};");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} && {tempVar}.Length > 0 ? {tempVar}[0] : null;");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} && {tempVar}.Length > 0 ? {tempVar}[0] : default;");
        }
    }

    /// <summary>
    /// Emits the assignment for a category parsed from a single configuration string.
    /// </summary>
    /// <remarks>
    /// This used to take a <c>typeName</c> alongside <c>parseMethod</c> and never read it — the
    /// declared type reached the output only through the caller's interpolated parse expression.
    /// That is what let the DateTime case pass a nullable type name unnoticed: the argument that
    /// looked like it carried the type was decorative, so nothing downstream could validate it.
    /// </remarks>
    private static void WriteParsedAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar, string parseMethod, bool needsCulture)
    {
        var tempVar = $"__{cfg.Name}Value";
        var cultureArg = needsCulture ? ", global::System.Globalization.CultureInfo.InvariantCulture" : "";
        var isRequired = !cfg.IsNullable && !cfg.HasDefaultValue;

        if (isRequired)
        {
            writer.WriteLine($"this.{cfg.Name} = {parseMethod}({configVar}[\"{cfg.Key}\"] ?? throw new global::System.InvalidOperationException(\"Configuration key '{cfg.Key}' is required but was not found.\"){cultureArg});");
        }
        else if (cfg.HasDefaultValue)
        {
            // A default wins over null on a nullable field too: it is the value when the key is missing.
            var defaultVal = FormatDefaultValue(cfg.DefaultValue, cfg.TypeCategory);
            // Cast a number to the field's type: `byte.Parse(s) : 200` has type int. CONFIG005 has checked it fits.
            if (cfg.TypeCategory == ConfigFieldTypeCategory.Numeric && cfg.DefaultValue is not null)
                defaultVal = $"({NonNullable(cfg.Type)})({defaultVal})";
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? {parseMethod}({tempVar}{cultureArg}) : {defaultVal};");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? {parseMethod}({tempVar}{cultureArg}) : null;");
        }
        else
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? {parseMethod}({tempVar}{cultureArg}) : default;");
        }
    }

    /// <summary>
    /// Strips a trailing <c>?</c> so a nullable declared type can be used as the target of a static
    /// <c>Parse</c> call. <c>global::System.DateTime?.Parse(...)</c> is not valid C#.
    /// </summary>
    /// <remarks>
    /// Safe as a string operation here because it is only applied to the fully-qualified name of a
    /// nullable value type, which cannot contain a <c>?</c> anywhere but the end.
    /// </remarks>
    private static string NonNullable(string type)
        => type.EndsWith("?") ? type.Substring(0, type.Length - 1) : type;

    private static void WriteNumericAssignment(IndentedTextWriter writer, ConfigField cfg, string configVar)
    {
        // Determine the correct parse method based on type
        var parseMethod = GetNumericParseMethod(cfg.Type);
        WriteParsedAssignment(writer, cfg, configVar, parseMethod, needsCulture: IsFloatingPoint(cfg.Type));
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
        else if (cfg.HasDefaultValue)
        {
            // A member name, or a number: an enum member arrives as its underlying value, of any integral type.
            var defaultVal = cfg.DefaultValue switch
            {
                null => "null",
                string name => $"{enumType}.{name}",
                var number => $"({enumType})({FormatDefaultValue(number, cfg.TypeCategory)})",
            };
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? global::System.Enum.Parse<{enumType}>({tempVar}) : {defaultVal};");
        }
        else if (cfg.IsNullable)
        {
            writer.WriteLine($"this.{cfg.Name} = {configVar}[\"{cfg.Key}\"] is string {tempVar} ? global::System.Enum.Parse<{enumType}>({tempVar}) : null;");
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
        else if (cfg.DefaultValue is string defaultUri)
        {
            writer.WriteLine($"this.{cfg.Name} = new global::System.Uri({configVar}[\"{cfg.Key}\"] ?? \"{EscapeString(defaultUri)}\");");
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

        // Invariant culture: the build machine's culture would otherwise write "3,5d".
        var invariant = global::System.Globalization.CultureInfo.InvariantCulture;
        return value switch
        {
            bool b => b ? "true" : "false",
            char c => $"'{EscapeChar(c)}'",
            string s => $"\"{EscapeString(s)}\"",
            float f => $"{f.ToString("R", invariant)}f",
            double d => $"{d.ToString("R", invariant)}d",
            decimal m => $"{m.ToString(invariant)}m",
            IFormattable number => number.ToString(null, invariant),
            _ => value.ToString() ?? "default"
        };
    }

    private static string EscapeChar(char c) => c switch
    {
        '\'' => "\\'",
        '\\' => "\\\\",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        _ => c.ToString(),
    };

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
