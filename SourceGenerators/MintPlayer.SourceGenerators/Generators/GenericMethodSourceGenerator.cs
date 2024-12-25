using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class GenericMethodSourceGenerator : IncrementalGenerator
    {
        public override IEnumerable<object> Setup(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
        {
            var methodsProvider = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, ct) =>
                {
                    return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 } methodDeclaration;
                },
                static (context, ct) =>
                {
                    if (context.Node is MethodDeclarationSyntax methodDeclaration)
                    {
                        var x = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, ct);
                        if (x is IMethodSymbol symbol)
                        {
                            var attr = symbol.GetAttributes();
                            var classDeclaration = (ClassDeclarationSyntax)methodDeclaration.Parent;
                            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);

                            var attributeSyntax = methodDeclaration.AttributeLists.SelectMany(l => l.Attributes).OfType<AttributeSyntax>()
                                .Select(a => new
                                {
                                    Attribute = a,
                                    Type = context.SemanticModel.GetTypeInfo(a, ct).ConvertedType
                                })
                                .FirstOrDefault(a => a.Type.Equals(context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(GenericMethodAttribute).FullName)));

                            if (int.TryParse(attributeSyntax.Attribute.ArgumentList.Arguments[0].Expression.ToFullString(), out var countValue))
                            {
                                return new Models.GenericMethodDeclaration
                                {
                                    Method = new Models.MethodDeclaration
                                    {
                                        MethodName = symbol.Name,
                                        ClassName = classSymbol.Name,
                                        MethodModifiers = methodDeclaration.Modifiers,
                                        ClassModifiers = classDeclaration.Modifiers,
                                        //ClassIsPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                        //MethodIsPartial = methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                                        //ClassIsStatic = classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                                        //MethodIsStatic = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                                        //GenericMethodAttribute = 
                                    },
                                    Count = countValue,
                                };
                            }
                        }
                    }
                    return null;
                }
            );

            var methodsSourceProvider = methodsProvider
                .Where(static (p) => p != null)
                .Combine(settingsProvider)
                .Select(static (providers, ct) => new Producers.GenericMethodProducer(providers.Left!, providers.Right.RootNamespace!));

            return [methodsSourceProvider];
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class GenericMethodAttribute : Attribute
    {
        public uint Count { get; set; } = 1;
        public Type? Transformer { get; set; }
    }
}
