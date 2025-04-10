using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class ClassNamesSourceGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var classDeclarationsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) =>
                {
                    return node is ClassDeclarationSyntax { } classDeclaration;
                },
                static (context, ct) =>
                {
                    if (context.Node is ClassDeclarationSyntax classDeclaration &&
                        context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol symbol)
                    {
                        return new Models.ClassDeclaration
                        {
                            Name = symbol.Name,
                        };
                    }
                    else
                    {
                        return default;
                    }
                }
            )
            .WithComparer(Models.ClassDeclarationValueComparer.Instance)
            .WhereEnabled(settingsProvider)
            .Collect();

        var fieldDeclarationsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is FieldDeclarationSyntax { AttributeLists.Count: > 0 } fieldDeclaration
                    && fieldDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
                static (context2, ct) =>
                {
                    if (context2.Node is FieldDeclarationSyntax fieldDeclaration &&
                        fieldDeclaration.Declaration.Variables.Count == 1 &&
                        context2.SemanticModel.GetDeclaredSymbol(fieldDeclaration.Declaration.Variables[0], ct) is IFieldSymbol symbol)
                    {
                        if (fieldDeclaration.Parent is ClassDeclarationSyntax classDeclaration)
                        {
                            var namespaceDeclaration = classDeclaration.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
                            var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);

                            return new Models.FieldDeclaration
                            {
                                Namespace = namespaceDeclaration.Name.ToString(),
                                FullyQualifiedClassName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                ClassName = classSymbol.Name,
                                Name = symbol.Name,
                                FullyQualifiedTypeName = symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                Type = symbol.Type.Name,
                            };

                        }
                    }
                    
                    return default;
                }
            )
            .WithComparer(Models.FieldDeclarationValueComparer.Instance)
            .WhereEnabled(settingsProvider)
            .Collect();

        var classNamesSourceProvider = classDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.ClassNamesProducer(declarations: p.Left.NotNull(), rootNamespace: p.Right.RootNamespace!));

        var classNameListSourceProvider = classDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new Producers.ClassNameListProducer(declarations: p.Left.NotNull(), rootNamespace: p.Right.RootNamespace!));

        var classNamesDiagnosticProvider = classDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static IDiagnosticReporter (p, ct) => new Producers.ClassNamesProducer(declarations: p.Left.NotNull(), rootNamespace: p.Right.RootNamespace!));

        var classNameListDiagnosticProvider = classDeclarationsProvider
            .Combine(settingsProvider)
            .Select(static IDiagnosticReporter (p, ct) => new Producers.ClassNameListProducer(declarations: p.Left.NotNull(), rootNamespace: p.Right.RootNamespace!));

        // Combine all Source Providers
        context.ProduceCode(classNamesSourceProvider, classNameListSourceProvider);
        context.ReportDiagnostics(classNamesDiagnosticProvider, classNameListDiagnosticProvider);
    }
}

//public static partial class Ext
//{
//    [GenericMethod(Count = 5, Transformer = typeof(GenericMethodTransformer))]
//    public static void RegisterSourceOutput(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action)
//    {
//    }

//    //    public static void RegisterSourceOutput<T1, T2>(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action, IncrementalValueProvider<T1> p1, IncrementalValueProvider<T2> p2)
//    //    {

//    //    }
//    //    public static void RegisterSourceOutput<T1, T2, T3>(this IncrementalGeneratorInitializationContext context, Action<SourceProductionContext, Producer> action, IncrementalValueProvider<T1> p1, IncrementalValueProvider<T2> p2, IncrementalValueProvider<T3> p3)
//    //    {

//    //    }
//}

//public class GenericMethodTransformer : IGenericMethodTransformer
//{
//    public string Transform(string name) => $"IncrementalValueProvider<{name}>";
//}
//public interface IGenericMethodTransformer
//{
//    string Transform(string name);
//}
