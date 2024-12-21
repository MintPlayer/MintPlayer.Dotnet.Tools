using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    internal class ConstructorInjectionGenerator : IncrementalGenerator
    {
        public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
        {
            var classProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, ct) => node is ClassDeclarationSyntax,
                    static (context2, ct) =>
                    {
                        if (context2.Node is ClassDeclarationSyntax classDeclaration)
                        {
                            var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);
                            if (classSymbol is INamedTypeSymbol namedTypeSymbol)
                            {
                                return new ClassDefinition
                                {
                                    ClassName = namedTypeSymbol.Name,
                                    ClassFQN = namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                };
                            }
                        }

                        return default;
                    }
                )
                .Collect();

            var classSourceProvider = classProvider
                .Combine(settingsProvider)
                .Select(static (p, ct) => new ConstructorProducer(p.Left, p.Right.RootNamespace!));

            // Combine all source providers
            var sourceProvider = classSourceProvider;

            // Generate code
            context.RegisterSourceOutput(sourceProvider, static (c, g) => g?.Produce(c));
        }
    }

    internal class ClassDefinition
    {
        public string? ClassName { get; set; }
        public string? ClassFQN { get; set; }
        //public List<string> Dependencies { get; set; } = [];
    }

    internal class ConstructorProducer : Producer
    {
        private readonly IEnumerable<ClassDefinition> classDefinitions;
        public ConstructorProducer(IEnumerable<ClassDefinition> classDefinitions, string rootNamespace) : base(rootNamespace)
        {
            this.classDefinitions = classDefinitions;
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
