using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    internal class ConstructorInjectionGenerator : IncrementalGenerator
    {
        public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
        {
            //var classProvider = context.SyntaxProvider
            //    .CreateSyntaxProvider(
            //        static (node, ct) => node is ClassDeclarationSyntax,
            //        static (context2, ct) =>
            //        {
            //            if (context2.Node is ClassDeclarationSyntax classDeclaration)
            //            {
            //                var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);
            //                if (classSymbol is INamedTypeSymbol namedTypeSymbol)
            //                {
            //                    var fields = namedTypeSymbol.get

            //                    return new ClassDefinition
            //                    {
            //                        ClassName = namedTypeSymbol.Name,
            //                        ClassFQN = namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            //                        Fields
            //                    };
            //                }
            //            }

            //            return default;
            //        }
            //    )
            //    .Collect();

            //var classSourceProvider = classProvider
            //    .Combine(settingsProvider)
            //    .Select(static (p, ct) => new ConstructorProducer(p.Left, p.Right.RootNamespace!));

            //// Combine all source providers
            //var sourceProvider = classSourceProvider;

            var fieldsProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => node is FieldDeclarationSyntax { Modifiers.Count: > 0 },
                    static (context2, ct) =>
                    {
                        if (context2.Node is FieldDeclarationSyntax fieldDeclaration &&
                            fieldDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                        {
                            var injectAttribute = context2.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Attributes.InjectAttribute).FullName);

                            var result = fieldDeclaration.Declaration.Variables
                                .Select(v => context2.SemanticModel.GetDeclaredSymbol(v))
                                .Select(sym => new
                                {
                                    FieldSymbol = sym,
                                    HasInjectAttribute = sym?.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, injectAttribute)),
                                })
                                .ToList();
                            //context2.SemanticModel.GetDeclaredSymbol

                            //var fieldSymbol = context2.SemanticModel.GetDeclaredSymbol(fieldDeclaration);
                            //if (fieldSymbol is null) return default;

                            //var injectAttributes = fieldSymbol.GetAttributes()
                            //    .Where(a =>
                            //        a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{typeof(Attributes.InjectAttribute).FullName}" &&
                            //        SymbolEqualityComparer.Default.Equals(a.AttributeClass, injectAttribute))
                            //        //a.AttributeClass.ContainingAssembly.Name == typeof(Attributes.InjectAttribute).Assembly.FullName)
                            //    .ToList();
                        }
                        return new FieldDefinition();
                    }
                )
                .Collect();

            var fieldsSourceProvider = fieldsProvider
                .Combine(settingsProvider)
                .Select(static (p, ct) => new ConstructorProducer(p.Left, p.Right.RootNamespace!));

            // Combine all source providers
            var sourceProvider = fieldsSourceProvider;

            // Generate code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }

    internal class ClassDefinition
    {
        public string? ClassName { get; set; }
        public string? ClassFQN { get; set; }
        public List<FieldDefinition> Fields { get; set; } = [];
    }

    internal class FieldDefinition
    {
        public string? FieldName { get; set; }
        public bool IsReadonly { get; set; }
        public AttributeDefinition[] Attributes { get; set; } = [];
    }

    internal class AttributeDefinition
    {

    }

    internal class ConstructorProducer : Producer
    {
        private readonly IEnumerable<FieldDefinition> fieldDefinitions;
        public ConstructorProducer(IEnumerable<FieldDefinition> fieldDefinitions, string rootNamespace) : base(rootNamespace)
        {
            this.fieldDefinitions = fieldDefinitions;
        }

        protected override ProducedSource? ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteLine(Header);
            writer.WriteLine();
            writer.WriteLine($"namespace {RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;

            writer.Indent--;
            writer.WriteLine("}");

            return new ProducedSource { FileName = "ConstructorInjection.g.cs" };
        }
    }
}
