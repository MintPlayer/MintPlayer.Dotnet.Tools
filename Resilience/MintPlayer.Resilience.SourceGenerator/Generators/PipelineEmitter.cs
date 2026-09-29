using System.CodeDom.Compiler;
using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using MintPlayer.Resilience.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

/// <summary>
/// Emits one pipeline class: the execution members, the flat method (or the forwarding to a runtime pipeline when
/// the pipeline contains hedging), the runtime snapshot class and, for a reloadable pipeline, its options class.
/// </summary>
/// <remarks>
/// The flat method mirrors the interpreter (<c>PipelineCore.RunAsync</c>) statement for statement: every strategy
/// is "enter; inner; exit" on the frame's outcome, a hook exception becomes the outcome, and the strategies that
/// are driven through <c>GeneratedStrategy</c> run the interpreter's own hooks. Retry, timeout and fallback are
/// inlined as ports of <c>RetryStrategy</c>, <c>TimeoutStrategy</c> and <c>FallbackStrategy</c>.
/// </remarks>
internal sealed partial class PipelineEmitter
{
    private const string Res = "global::MintPlayer.Resilience.";
    private const string P = "global::MintPlayer.Resilience.Pipeline.";
    private const string Support = P + "GeneratedPipelineSupport";
    private const string VT = "global::System.Threading.Tasks.ValueTask";
    private const string CT = "global::System.Threading.CancellationToken";
    private const string RC = Res + "ResilienceContext";
    private const string Func = "global::System.Func";
    private const string Act = "global::System.Action";
    private const string TS = "global::System.TimeSpan";
    private const string Ex = "global::System.Exception";
    private const string Volatile = "global::System.Threading.Volatile";
    private const string GeneratedCode = "[global::System.CodeDom.Compiler.GeneratedCode(\"MintPlayer.Resilience.SourceGenerator\", \"11.0.0\")]";

    private readonly IndentedTextWriter _w;
    private readonly PipelineModel _m;

    public PipelineEmitter(IndentedTextWriter writer, PipelineModel model)
    {
        _w = writer;
        _m = model;
    }

    private bool IsGeneric => _m.ResultType is null;

    /// <summary>The result type in the flat method: the concrete type, or the method's type parameter.</summary>
    private string R => _m.ResultType ?? "TResult";

    private string Static => _m.IsInstance ? string.Empty : "static ";

    private string Self => _m.FullName;

    private string OptionsName => _m.ClassName + "Options";

    private string OptionsFull => _m.FullName.Substring(0, _m.FullName.Length - _m.ClassName.Length) + OptionsName;

    private bool Interpreted => _m.Strategies.Any(s => StrategySchema.Of(s.Kind).Forks);

    private int Count => _m.Strategies.Count;

    private string Interface => $"{Res}IGeneratedResiliencePipeline<{Self}>";

    private string ReloadInterface => $"{Res}IReloadableResiliencePipeline<{Self}, {OptionsFull}>";

    public void Emit()
    {
        var closers = new List<IDisposable>();
        foreach (var containing in _m.ContainingTypes)
        {
            closers.Add(_w.OpenBlock($"partial {containing.Keyword} {containing.Name}"));
        }

        EmitClass();
        if (_m.Reloadable)
        {
            _w.WriteLine();
            EmitOptions();
        }

        for (var i = closers.Count - 1; i >= 0; i--)
        {
            closers[i].Dispose();
        }
    }

    private void L(string line) => _w.WriteLine(line);

    private void Blank() => _w.WriteLineNoTabs(string.Empty);

    private IDisposable Open(string line) => _w.OpenBlock(line);

    private static string Str(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private void EmitClass()
    {
        var interfaces = _m.Reloadable ? ReloadInterface : Interface;
        L(GeneratedCode);
        using (Open($"partial class {_m.ClassName} : {interfaces}"))
        {
            EmitInfrastructure();
            Blank();
            if (_m.Reloadable)
            {
                EmitReload();
                Blank();
            }

            EmitExecutionMembers();
            Blank();
            if (!Interpreted)
            {
                EmitFlatMethod();
                Blank();
            }

            EmitRuntime();
        }
    }

    // ---------------------------------------------------------------- infrastructure: name, factory, clock, snapshot

    private void EmitInfrastructure()
    {
        var owner = _m.IsInstance ? ", this" : string.Empty;
        var defaultOptions = _m.Reloadable ? $", new {OptionsFull}()" : string.Empty;

        L($"private const int __StrategyCount = {Count.ToString(CultureInfo.InvariantCulture)};");
        L($"private {Static}readonly global::System.Threading.Lock __lock = new();");
        L($"private {Static}__Runtime? __runtime;");
        Blank();
        L("/// <summary>Gets the name of the pipeline, used by telemetry and as its configuration key.</summary>");
        L($"public static string PipelineName => {Str(_m.PipelineName)};");
        Blank();
        L($"static bool {Interface}.IsInstancePipeline => {(_m.IsInstance ? "true" : "false")};");
        Blank();
        using (Open($"static {Self} {Interface}.Create(global::System.IServiceProvider services)"))
        {
            L("global::System.ArgumentNullException.ThrowIfNull(services);");
            var arguments = _m.IsInstance
                ? string.Join(", ", _m.ConstructorParameters.Select(p => p.Default is null
                    ? $"__Resolve<{p.Type}>(services, {Str(p.Name)})"
                    : $"__ResolveOrDefault<{p.Type}>(services, {p.Default})"))
                : string.Empty;
            L($"return new {Self}({arguments});");
        }

        Blank();
        using (Open($"static void {Interface}.UseTimeProvider({Self} instance, global::System.TimeProvider timeProvider, {Func}<double>? randomizer)"))
        {
            if (_m.IsInstance)
            {
                L("global::System.ArgumentNullException.ThrowIfNull(instance);");
                L("instance.UseTimeProvider(timeProvider, randomizer);");
            }
            else
            {
                L("UseTimeProvider(timeProvider, randomizer);");
            }
        }

        Blank();
        L(_m.IsInstance
            ? "/// <summary>Replaces the clock of this pipeline instance (and, for tests, the random source of jitter and chaos). The strategy state is recreated: call it before the first execution.</summary>"
            : "/// <summary>Replaces the clock of the pipeline (and, for tests, the random source of jitter and chaos). The strategy state is recreated: call it at startup, before the first execution.</summary>");
        L("/// <param name=\"timeProvider\">The clock of every strategy.</param>");
        L("/// <param name=\"randomizer\">Returns a value in [0, 1); <see langword=\"null\"/> for the shared random source.</param>");
        using (Open($"public {Static}void UseTimeProvider(global::System.TimeProvider timeProvider, {Func}<double>? randomizer = null)"))
        {
            L("global::System.ArgumentNullException.ThrowIfNull(timeProvider);");
            using (Open("lock (__lock)"))
            {
                L($"var current = {Volatile}.Read(ref __runtime);");
                var options = _m.Reloadable ? $", current?.Options ?? new {OptionsFull}()" : string.Empty;
                L("// A new clock means new state: nothing is reused, but the breakers' attachments move to the new ones.");
                L($"{Volatile}.Write(ref __runtime, new __Runtime(timeProvider, randomizer{owner}{options}, current, reuse: false));");
            }
        }

        Blank();
        L($"private {Static}__Runtime __Current => {Volatile}.Read(ref __runtime) ?? __Initialize();");
        Blank();
        using (Open($"private {Static}__Runtime __Initialize()"))
        {
            using (Open("lock (__lock)"))
            {
                L($"var current = {Volatile}.Read(ref __runtime);");
                using (Open("if (current is null)"))
                {
                    L($"current = new __Runtime(global::System.TimeProvider.System, null{owner}{defaultOptions}, null, reuse: false);");
                    L($"{Volatile}.Write(ref __runtime, current);");
                }

                L("return current;");
            }
        }

        if (_m.IsInstance)
        {
            Blank();
            L("private static TArg __Resolve<TArg>(global::System.IServiceProvider services, string parameter)");
            L($"    => services.GetService(typeof(TArg)) is TArg value ? value : throw new global::System.InvalidOperationException($\"Unable to resolve a service of type '{{typeof(TArg)}}' for the parameter '{{parameter}}' of {_m.ClassName}.\");");
            Blank();
            L("private static TArg __ResolveOrDefault<TArg>(global::System.IServiceProvider services, TArg fallback)");
            L("    => services.GetService(typeof(TArg)) is TArg value ? value : fallback;");
        }
    }

    private void EmitReload()
    {
        var instance = _m.IsInstance ? "instance." : string.Empty;
        L("/// <summary>Gets the configuration section bound by default.</summary>");
        L($"public static string DefaultSectionPath => {Str("Resilience:" + _m.PipelineName)};");
        Blank();
        using (Open($"static bool {ReloadInterface}.TryApply({Self} instance, {OptionsFull} options, out string? error)"))
        {
            if (_m.IsInstance)
            {
                L("global::System.ArgumentNullException.ThrowIfNull(instance);");
            }

            L($"return {instance}TryApply(options, out error);");
        }

        Blank();
        L("/// <summary>");
        L("/// Validates <paramref name=\"options\"/> with the runtime builder's rules and publishes them as the new snapshot. In-flight");
        L("/// executions keep the values they started with. A breaker, limiter or chaos strategy whose section is unchanged keeps its state.");
        L("/// </summary>");
        L("/// <param name=\"options\">The new values; not copied, so do not change them afterwards.</param>");
        L("/// <param name=\"error\">Why the values were rejected, or <see langword=\"null\"/>.</param>");
        L("/// <returns><see langword=\"false\"/> when the values are invalid; the previous snapshot then stays live.</returns>");
        using (Open($"public {Static}bool TryApply({OptionsFull} options, out string? error)"))
        {
            L("global::System.ArgumentNullException.ThrowIfNull(options);");
            L("_ = __Current;");
            using (Open("lock (__lock)"))
            {
                L($"var current = {Volatile}.Read(ref __runtime)!;");
                L("__Runtime next;");
                using (Open("try"))
                {
                    L($"next = new __Runtime(current.TimeProvider, current.Randomizer{(_m.IsInstance ? ", this" : string.Empty)}, options, current, reuse: true);");
                }

                using (Open($"catch ({Ex} exception)"))
                {
                    L("error = exception.Message;");
                    L("return false;");
                }

                L($"{Volatile}.Write(ref __runtime, next);");
                L("error = null;");
                L("return true;");
            }
        }
    }

    // ---------------------------------------------------------------- the options class of a reloadable pipeline

    private void EmitOptions()
    {
        L($"/// <summary>The reloadable values of <see cref=\"{Self}\"/>, bound from the configuration section {EscapeXml(Str("Resilience:" + _m.PipelineName))} by default. The defaults are the attribute values.</summary>");
        L(GeneratedCode);
        using (Open($"{_m.Accessibility} sealed class {OptionsName}"))
        {
            var first = true;
            foreach (var strategy in _m.Strategies)
            {
                var spec = StrategySchema.Of(strategy.Kind);
                if (spec.Values.Length == 0)
                {
                    continue;
                }

                if (!first)
                {
                    Blank();
                }

                first = false;
                var initializer = string.Join(", ", spec.Values.Select(v => $"{v.Option} = {Literal(strategy, v)}"));
                L($"/// <summary>Gets or sets the values of the {EscapeXml(strategy.Key)} strategy ({spec.DefaultKey}).</summary>");
                L($"public {spec.DefaultKey}Section {strategy.Key} {{ get; set; }} = new() {{ {initializer} }};");
            }

            foreach (var spec in _m.Strategies.Select(s => StrategySchema.Of(s.Kind)).Where(s => s.Values.Length > 0).Distinct())
            {
                Blank();
                L($"/// <summary>The reloadable values of a {spec.DefaultKey} strategy; the defaults are the runtime option defaults.</summary>");
                using (Open($"public sealed class {spec.DefaultKey}Section"))
                {
                    var firstValue = true;
                    foreach (var value in spec.Values)
                    {
                        if (!firstValue)
                        {
                            Blank();
                        }

                        firstValue = false;
                        L($"/// <summary>Gets or sets the {value.Option} option (attribute property {value.Attribute}).</summary>");
                        L($"public {SectionType(value)} {value.Option} {{ get; set; }} = {DefaultLiteral(value)};");
                    }
                }
            }
        }
    }

    private static string EscapeXml(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ---------------------------------------------------------------- values

    private static string SectionType(ValueSpec value) => value.Type switch
    {
        SettingType.Int => "int",
        SettingType.Double => "double",
        SettingType.Bool => "bool",
        SettingType.Enum => value.EnumType!,
        SettingType.Ms => TS,
        _ => TS + "?",
    };

    /// <summary>The value of a strategy setting as a literal: the attribute's, else the default.</summary>
    private static string Literal(StrategyModel strategy, ValueSpec value)
    {
        var setting = strategy.Settings.FirstOrDefault(s => s.Key == value.Attribute);
        if (setting is null)
        {
            return DefaultLiteral(value);
        }

        return value.Type switch
        {
            SettingType.Ms => Milliseconds(setting.Integer ?? 0),
            SettingType.NullableMs => setting.Integer is < 0 ? $"({TS}?)null" : $"({TS}?){Milliseconds(setting.Integer ?? 0)}",
            _ => setting.Literal,
        };
    }

    private static string DefaultLiteral(ValueSpec value) => value.Type switch
    {
        SettingType.Int => ((long)value.Default).ToString(CultureInfo.InvariantCulture),
        SettingType.Double => PipelineParser.FormatDouble(value.Default),
        SettingType.Bool => value.Default != 0 ? "true" : "false",
        SettingType.Enum => $"({value.EnumType}){((long)value.Default).ToString(CultureInfo.InvariantCulture)}",
        SettingType.Ms => Milliseconds((long)value.Default),
        _ => value.Default < 0 ? $"({TS}?)null" : $"({TS}?){Milliseconds((long)value.Default)}",
    };

    private static string Milliseconds(long ms) => $"{TS}.FromMilliseconds({ms.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>A value as read by the flat method: a folded constant, or the snapshot's field for a reloadable pipeline.</summary>
    private string FlatValue(StrategyModel strategy, string option)
    {
        var value = StrategySchema.Of(strategy.Kind).Values.First(v => v.Option == option);
        return _m.Reloadable ? $"rt.{Field(strategy, option)}" : Literal(strategy, value);
    }

    /// <summary>A value as read while building the snapshot: a literal, or the new options for a reloadable pipeline.</summary>
    private string BuildValue(StrategyModel strategy, ValueSpec value)
        => _m.Reloadable ? $"options.{strategy.Key}.{value.Option}" : Literal(strategy, value);

    private static string Field(StrategyModel strategy, string option) => $"V{strategy.Index.ToString(CultureInfo.InvariantCulture)}_{option}";

    private static HookModel? Hook(StrategyModel strategy, string slot) => strategy.Hooks.FirstOrDefault(h => h.Slot == slot);

    private static MemberRefModel? Member(StrategyModel strategy, string slot) => strategy.Members.FirstOrDefault(m => m.Slot == slot);

    private static string I(StrategyModel strategy) => strategy.Index.ToString(CultureInfo.InvariantCulture);
}
