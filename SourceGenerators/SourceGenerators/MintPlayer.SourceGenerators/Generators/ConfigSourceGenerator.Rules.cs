using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Generators;

public static partial class DiagnosticRules
{
    private const string ConfigCategory = "MintPlayer.SourceGenerators.Config";

    #region CONFIG diagnostics

    public static readonly DiagnosticDescriptor ConfigNonPartialClass = new(
        id: "CONFIG001",
        title: "Non-partial class",
        messageFormat: "Class '{0}' must be partial to use [Config] attribute",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigEmptyKey = new(
        id: "CONFIG002",
        title: "Empty configuration key",
        messageFormat: "Configuration key cannot be empty or whitespace",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigUnsupportedType = new(
        id: "CONFIG003",
        title: "Unsupported type",
        messageFormat: "Type '{0}' is not supported for [Config]. Supported: primitives, enums, DateTime, TimeSpan, Guid, Uri, and POCO classes",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigDefaultValueTypeMismatch = new(
        id: "CONFIG005",
        title: "Default value type mismatch",
        messageFormat: "DefaultValue type '{0}' is not compatible with field type '{1}'",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigConflictWithConnectionString = new(
        id: "CONFIG006",
        title: "Conflicting attributes",
        messageFormat: "Field '{0}' cannot have both [Config] and [ConnectionString] attributes",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigDuplicateKey = new(
        id: "CONFIG007",
        title: "Duplicate configuration key",
        messageFormat: "Configuration key '{0}' is used multiple times in class '{1}'",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigConflictWithInject = new(
        id: "CONFIG008",
        title: "Conflicting with Inject",
        messageFormat: "Field '{0}' cannot have both [Config] and [Inject] attributes",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    #endregion

    #region CONNSTR diagnostics

    public static readonly DiagnosticDescriptor ConnectionStringEmptyName = new(
        id: "CONNSTR001",
        title: "Empty connection string name",
        messageFormat: "Connection string name cannot be empty or whitespace",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConnectionStringInvalidType = new(
        id: "CONNSTR002",
        title: "Invalid field type",
        messageFormat: "[ConnectionString] can only be applied to string fields. Field '{0}' has type '{1}'",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConnectionStringConflictWithInject = new(
        id: "CONNSTR003",
        title: "Conflicting with Inject",
        messageFormat: "Field '{0}' cannot have both [ConnectionString] and [Inject] attributes",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    #endregion

    #region OPTIONS diagnostics

    public static readonly DiagnosticDescriptor OptionsInvalidType = new(
        id: "OPTIONS001",
        title: "Invalid options type",
        messageFormat: "[Options] requires field type IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>. Field '{0}' has type '{1}'",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionsBindingHint = new(
        id: "OPTIONS002",
        title: "Options binding hint",
        messageFormat: "Consider registering options binding: services.Configure<{0}>(configuration.GetSection(\"{1}\"))",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor OptionsConflictWithInject = new(
        id: "OPTIONS003",
        title: "Conflicting with Inject",
        messageFormat: "Field '{0}' cannot have both [Options] and [Inject] attributes. [Options] already handles injection",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionsConflictWithConfig = new(
        id: "OPTIONS004",
        title: "Conflicting with Config",
        messageFormat: "Field '{0}' cannot have both [Options] and [Config] attributes",
        category: ConfigCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    #endregion
}
