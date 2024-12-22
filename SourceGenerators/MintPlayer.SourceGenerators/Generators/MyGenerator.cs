using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class InjectSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
            context.RegisterForSyntaxNotifications(() => new InjectSyntaxReceiver());
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
        }

        public void Execute(GeneratorExecutionContext context)
        {
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
            if (context.SyntaxReceiver is not InjectSyntaxReceiver receiver)
                return;
#pragma warning restore RS1035 // Do not use APIs banned for analyzers

            foreach (var classDeclaration in receiver.CandidateClasses)
            {
                // Generate code for each class with [Inject] attributes
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
                var semanticModel = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
                var source = GenerateConstructorForClass(classDeclaration, semanticModel);
                if (!string.IsNullOrEmpty(source))
                {
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
                    context.AddSource($"{classDeclaration.Identifier.Text}_Inject.g.cs", source);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
                }
            }
        }

        private string GenerateConstructorForClass(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
        {
            var namespaceName = GetNamespace(classDeclaration);
            var className = classDeclaration.Identifier.Text;

            // Find all fields with [Inject]
            var injectFields = classDeclaration.Members
                .OfType<FieldDeclarationSyntax>()
                .Where(field => field.AttributeLists
                    .SelectMany(attrs => attrs.Attributes)
                    .Any(attr => semanticModel.GetTypeInfo(attr).Type?.Name == "InjectAttribute"))
                .ToList();

            if (!injectFields.Any())
                return string.Empty;

            var constructorParams = new List<string>();
            var assignments = new List<string>();

            foreach (var field in injectFields)
            {
                var type = field.Declaration.Type.ToString();
                var name = field.Declaration.Variables.First().Identifier.Text;

                constructorParams.Add($"{type} {name}");
                assignments.Add($"this.{name} = {name};");
            }

            var baseConstructorParams = FindBaseConstructorParams(classDeclaration, semanticModel);

            var sb = new StringBuilder();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {className}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {className}({string.Join(", ", constructorParams.Concat(baseConstructorParams))})");
            sb.AppendLine(baseConstructorParams.Any() ? $"            : base({string.Join(", ", baseConstructorParams)})" : "");
            sb.AppendLine("        {");
            foreach (var assignment in assignments)
            {
                sb.AppendLine($"            {assignment}");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private IEnumerable<string> FindBaseConstructorParams(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
        {
            var baseType = classDeclaration.BaseList?.Types.FirstOrDefault();
            if (baseType == null)
                return Enumerable.Empty<string>();

            var baseSymbol = semanticModel.GetSymbolInfo(baseType.Type).Symbol as INamedTypeSymbol;
            if (baseSymbol == null)
                return Enumerable.Empty<string>();

            // Find base constructor and its parameters
            var constructor = baseSymbol.Constructors.FirstOrDefault(c => c.Parameters.Any());
            return constructor?.Parameters.Select(param => param.Name) ?? Enumerable.Empty<string>();
        }

        private string GetNamespace(ClassDeclarationSyntax classDeclaration)
        {
            var namespaceDeclaration = classDeclaration.Parent as NamespaceDeclarationSyntax;
            return namespaceDeclaration?.Name.ToString() ?? "GlobalNamespace";
        }

        private class InjectSyntaxReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is ClassDeclarationSyntax classDeclaration &&
                    classDeclaration.Members.OfType<FieldDeclarationSyntax>()
                        .Any(field => field.AttributeLists.Any()))
                {
                    CandidateClasses.Add(classDeclaration);
                }
            }
        }
    }
}
