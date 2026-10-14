using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.Resilience.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

/// <summary>Reduces a <c>[ResiliencePipeline]</c> class symbol to a <see cref="PipelineModel"/>.</summary>
internal static class PipelineParser
{
    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static PipelineModel Describe(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var model = new PipelineModel
        {
            Namespace = type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty,
            ClassName = type.Name,
            FullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility = AccessibilityKeyword(type.DeclaredAccessibility),
        };

        model.SkipReason = ShapeProblem(type);
        model.ContainingTypes = ContainingTypes(type).ToEquatableArray();

        // The pipeline attribute: generic form fixes the result type.
        string? resultType = null;
        foreach (var attribute in type.GetAttributes())
        {
            var name = MetadataName(attribute.AttributeClass);
            if (name != StrategySchema.PipelineAttribute && name != StrategySchema.GenericPipelineAttribute)
            {
                continue;
            }

            if (name == StrategySchema.GenericPipelineAttribute && attribute.AttributeClass!.TypeArguments.Length == 1)
            {
                resultType = attribute.AttributeClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "PooledAsync" when argument.Value.Value is bool pooled:
                        model.PooledAsync = pooled;
                        break;
                    case "Reloadable" when argument.Value.Value is bool reloadable:
                        model.Reloadable = reloadable;
                        break;
                    case "Name" when argument.Value.Value is string pipelineName && !string.IsNullOrWhiteSpace(pipelineName):
                        model.PipelineName = pipelineName;
                        break;
                }
            }
        }

        if (string.IsNullOrEmpty(model.PipelineName))
        {
            model.PipelineName = type.Name;
        }

        // The strategy attributes, in declaration order (plan S1: one declaration keeps source order).
        var strategies = new List<StrategyModel>();
        var hooksBySlot = new List<Dictionary<string, string>>();
        var membersBySlot = new List<Dictionary<string, string>>();
        foreach (var attribute in type.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = MetadataName(attribute.AttributeClass);
            if (name == StrategySchema.BudgetAttribute)
            {
                model.Budget = DescribeBudget(attribute);
                continue;
            }

            if (StrategySchema.ByAttribute(name) is not { } spec)
            {
                continue;
            }

            var strategy = new StrategyModel { Kind = spec.Kind, Index = strategies.Count };
            var settings = new List<SettingModel>();
            var hooks = new Dictionary<string, string>(StringComparer.Ordinal);
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var argument in attribute.NamedArguments)
            {
                var key = argument.Key;
                var value = argument.Value;
                if (key == "Name")
                {
                    strategy.Name = value.Value as string;
                }
                else if (spec.Values.Any(v => v.Attribute == key) || key == "FaultType")
                {
                    if (Literal(value) is { } setting)
                    {
                        setting.Key = key;
                        settings.Add(setting);
                    }
                }
                else if (value.Value is string reference && !string.IsNullOrWhiteSpace(reference))
                {
                    if (spec.Members.Contains(key) && !IsMethod(type, reference))
                    {
                        members[key] = reference;
                    }
                    else if (spec.Hooks.Any(h => h.Slot == key))
                    {
                        hooks[key] = reference;
                    }
                }
            }

            strategy.Settings = settings.ToEquatableArray();
            strategies.Add(strategy);
            hooksBySlot.Add(hooks);
            membersBySlot.Add(members);
        }

        // Hook attributes on methods bind to the only strategy of their kind, or to the one they name.
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attribute in method.GetAttributes())
            {
                var name = MetadataName(attribute.AttributeClass);
                foreach (var (hookAttribute, kind, slot) in StrategySchema.HookAttributes)
                {
                    if (hookAttribute != name)
                    {
                        continue;
                    }

                    var target = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
                    var candidates = strategies.Where(s => s.Kind == kind && (target is null || s.Name == target)).ToList();
                    if (candidates.Count == 1 && !hooksBySlot[candidates[0].Index].ContainsKey(slot))
                    {
                        hooksBySlot[candidates[0].Index][slot] = method.Name;
                    }
                }
            }
        }

        // Resolve hooks and members, and infer the result type from the typed hooks.
        var inferred = new List<string>();
        var usesInstance = false;
        foreach (var strategy in strategies)
        {
            var spec = StrategySchema.Of(strategy.Kind);
            var hooks = new List<HookModel>();
            foreach (var hookSpec in spec.Hooks)
            {
                if (!hooksBySlot[strategy.Index].TryGetValue(hookSpec.Slot, out var methodName))
                {
                    continue;
                }

                if (FindMethod(type, methodName) is not { } method)
                {
                    model.SkipReason ??= $"the hook '{methodName}' of {spec.DefaultKey}.{hookSpec.Slot} is not a method of the class";
                    continue;
                }

                var hook = DescribeHook(hookSpec, method, inferred);
                usesInstance |= !hook.IsStatic;
                hooks.Add(hook);
            }

            var members = new List<MemberRefModel>();
            foreach (var pair in membersBySlot[strategy.Index].OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var member = type.GetMembers(pair.Value).FirstOrDefault(m => m is IFieldSymbol or IPropertySymbol);
                if (member is null)
                {
                    model.SkipReason ??= $"the member '{pair.Value}' of {spec.DefaultKey}.{pair.Key} is not a field or property of the class";
                    continue;
                }

                usesInstance |= !member.IsStatic;
                members.Add(new MemberRefModel { Slot = pair.Key, Member = member.Name, IsStatic = member.IsStatic });
            }

            strategy.Hooks = hooks.ToEquatableArray();
            strategy.Members = members.ToEquatableArray();
        }

        model.ResultType = resultType ?? inferred.FirstOrDefault();

        // Unique, identifier-safe keys for the reloadable options class.
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strategy in strategies)
        {
            var baseKey = Identifier(strategy.Name) ?? StrategySchema.Of(strategy.Kind).DefaultKey;
            var key = baseKey;
            for (var n = 2; !used.Add(key); n++)
            {
                key = baseKey + n.ToString(CultureInfo.InvariantCulture);
            }

            strategy.Key = key;
        }

        model.Strategies = strategies.ToEquatableArray();

        // The DI form: an instance hook or member, or a constructor that takes parameters.
        var constructor = type.InstanceConstructors
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length > 0)
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        model.IsInstance = usesInstance || constructor is { Parameters.Length: > 0 };
        if (model.IsInstance && constructor is not null)
        {
            model.ConstructorParameters = constructor.Parameters
                .Select(p => new ConstructorParameterModel
                {
                    Name = p.Name,
                    Type = p.Type.ToDisplayString(Qualified),
                    Default = p.HasExplicitDefaultValue ? DefaultLiteral(p) : null,
                })
                .ToEquatableArray();
        }

        model.SkipReason ??= CombinationProblem(model);
        return model;
    }

    private static string? ShapeProblem(INamedTypeSymbol type)
    {
        if (type.IsStatic)
        {
            return "a declarative pipeline must be a (sealed) partial class, not a static class";
        }

        if (type.TypeParameters.Length > 0)
        {
            return "a declarative pipeline cannot be generic";
        }

        if (!IsPartial(type))
        {
            return "a declarative pipeline must be partial";
        }

        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (containing.TypeParameters.Length > 0 || !IsPartial(containing))
            {
                return "the types containing a declarative pipeline must be partial and non-generic";
            }
        }

        return null;
    }

    private static string? CombinationProblem(PipelineModel model)
    {
        foreach (var strategy in model.Strategies)
        {
            var spec = StrategySchema.Of(strategy.Kind);
            if (model.ResultType is null && spec.TypedOnly)
            {
                return $"{spec.DefaultKey} needs a typed pipeline ([ResiliencePipeline<TResult>] or a typed hook)";
            }

            bool Has(string slot) => strategy.Hooks.Any(h => h.Slot == slot);
            switch (strategy.Kind)
            {
                case StrategyKind.Fallback when !Has("FallbackAction"):
                    return "a fallback needs a FallbackAction";
                case StrategyKind.ChaosOutcome when !Has("OutcomeGenerator"):
                    return "an outcome chaos strategy needs an OutcomeGenerator";
                case StrategyKind.ChaosBehavior when !Has("BehaviorGenerator"):
                    return "a behavior chaos strategy needs a BehaviorGenerator";
                case StrategyKind.ChaosFault when !Has("FaultGenerator") && !strategy.Settings.Any(s => s.Key == "FaultType"):
                    return "a fault chaos strategy needs a FaultType or a FaultGenerator";
            }
        }

        return null;
    }

    private static HookModel DescribeHook(HookSpec spec, IMethodSymbol method, List<string> inferred)
    {
        var hook = new HookModel
        {
            Slot = spec.Slot,
            Method = method.Name,
            IsStatic = method.IsStatic,
            IsGeneric = method.TypeParameters.Length == 1,
        };

        ITypeSymbol? resultCarrier = null;
        if (method.Parameters.Length == 0)
        {
            hook.Parameter = HookParameter.None;
        }
        else if (method.Parameters[0].Type is INamedTypeSymbol { IsGenericType: true } parameter && MetadataName(parameter.OriginalDefinition) == StrategySchema.OutcomeType)
        {
            hook.Parameter = HookParameter.Outcome;
            resultCarrier = parameter.TypeArguments[0];
        }
        else
        {
            hook.Parameter = HookParameter.Arguments;
            if (spec.ResultTyped && method.Parameters[0].Type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } arguments)
            {
                resultCarrier = arguments.TypeArguments[0];
            }
        }

        var returns = method.ReturnType;
        switch (spec.Return)
        {
            case SlotReturn.Event or SlotReturn.Behavior:
                hook.Return = returns.SpecialType == SpecialType.System_Void ? HookReturn.Void : HookReturn.Direct;
                break;
            case SlotReturn.FallbackAction:
                if (IsGeneric(returns, "System.Threading.Tasks.ValueTask`1", out var inner))
                {
                    if (IsGeneric(inner!, StrategySchema.OutcomeType, out var result))
                    {
                        hook.Return = HookReturn.ValueTaskOutcome;
                        resultCarrier ??= result;
                    }
                    else
                    {
                        hook.Return = HookReturn.ValueTaskResult;
                        resultCarrier ??= inner;
                    }
                }
                else if (IsGeneric(returns, StrategySchema.OutcomeType, out var outcomeResult))
                {
                    hook.Return = HookReturn.Outcome;
                    resultCarrier ??= outcomeResult;
                }
                else
                {
                    hook.Return = HookReturn.Result;
                    resultCarrier ??= returns;
                }

                break;
            case SlotReturn.Outcome:
                // Outcome<T>? M(OutcomeGeneratorArguments): the result type is inside the nullable.
                var unwrapped = returns is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : returns;
                if (IsGeneric(unwrapped, StrategySchema.OutcomeType, out var generated))
                {
                    resultCarrier = generated;
                }

                hook.Return = HookReturn.Direct;
                break;
            default:
                hook.Return = HookReturn.Direct;
                break;
        }

        if (resultCarrier is not null and not ITypeParameterSymbol)
        {
            inferred.Add(resultCarrier.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return hook;
    }

    private static BudgetModel DescribeBudget(AttributeData attribute)
    {
        var budget = new BudgetModel();
        foreach (var argument in attribute.NamedArguments)
        {
            if (Literal(argument.Value) is not { } literal)
            {
                continue;
            }

            switch (argument.Key)
            {
                case "RetryRatio":
                    budget.RetryRatio = literal.Literal;
                    break;
                case "MinRetriesPerSecond":
                    budget.MinRetriesPerSecond = literal.Literal;
                    break;
                case "TimeToLiveMs":
                    budget.TimeToLiveMs = literal.Literal;
                    break;
            }
        }

        return budget;
    }

    /// <summary>A typed constant as a C# literal.</summary>
    private static SettingModel? Literal(TypedConstant value)
    {
        if (value.IsNull)
        {
            return null;
        }

        if (value.Kind == TypedConstantKind.Type && value.Value is ITypeSymbol typeValue)
        {
            return new SettingModel { Literal = typeValue.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) };
        }

        if (value.Kind == TypedConstantKind.Enum && value.Type is not null)
        {
            var number = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
            return new SettingModel
            {
                Literal = $"({value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){number.ToString(CultureInfo.InvariantCulture)}",
                Integer = number,
            };
        }

        return value.Value switch
        {
            bool b => new SettingModel { Literal = b ? "true" : "false", Integer = b ? 1 : 0 },
            int i => new SettingModel { Literal = i.ToString(CultureInfo.InvariantCulture), Integer = i },
            long l => new SettingModel { Literal = l.ToString(CultureInfo.InvariantCulture) + "L", Integer = l },
            double d => new SettingModel { Literal = FormatDouble(d) },
            float f => new SettingModel { Literal = FormatDouble(f) },
            _ => null,
        };
    }

    public static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }

    private static string DefaultLiteral(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            return "default!";
        }

        if (parameter.Type.TypeKind == TypeKind.Enum)
        {
            return $"({parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}";
        }

        return value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            bool b => b ? "true" : "false",
            double d => FormatDouble(d),
            float f => FormatDouble(f) + "",
            IFormattable formattable => $"({parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){formattable.ToString(null, CultureInfo.InvariantCulture)}",
            _ => "default!",
        };
    }

    private static IEnumerable<ContainingTypeModel> ContainingTypes(INamedTypeSymbol type)
    {
        var chain = new List<ContainingTypeModel>();
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            chain.Insert(0, new ContainingTypeModel
            {
                Name = containing.Name,
                Keyword = containing switch
                {
                    { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
                    { IsRecord: true } => "record",
                    { TypeKind: TypeKind.Struct } => "struct",
                    { TypeKind: TypeKind.Interface } => "interface",
                    _ => "class",
                },
            });
        }

        return chain;
    }

    private static bool IsPartial(INamedTypeSymbol type)
        => type.DeclaringSyntaxReferences.Length > 0
            && type.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static IMethodSymbol? FindMethod(INamedTypeSymbol type, string name)
        => type.GetMembers(name).OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.Parameters.Length <= 1)
            .OrderByDescending(m => m.Parameters.Length)
            .FirstOrDefault();

    private static bool IsMethod(INamedTypeSymbol type, string name) => type.GetMembers(name).Any(m => m is IMethodSymbol);

    private static bool IsGeneric(ITypeSymbol type, string metadataName, out ITypeSymbol? argument)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named && MetadataName(named.OriginalDefinition) == metadataName)
        {
            argument = named.TypeArguments[0];
            return true;
        }

        argument = null;
        return false;
    }

    /// <summary>The metadata name including the namespace, e.g. <c>MintPlayer.Resilience.ResiliencePipelineAttribute`1</c>.</summary>
    public static string MetadataName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return string.Empty;
        }

        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() + "." : string.Empty;
        return ns + type.MetadataName;
    }

    private static string? Identifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var chars = name!.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var identifier = new string(chars);
        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    private static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "internal",
    };
}
