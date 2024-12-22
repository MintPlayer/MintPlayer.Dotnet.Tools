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
            context.RegisterForSyntaxNotifications(() => new InjectSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not InjectSyntaxReceiver receiver)
                return;

            foreach (var classDeclaration in receiver.CandidateClasses)
            {
                var semanticModel = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var source = GenerateConstructorForClass(classDeclaration, semanticModel, context.Compilation);
                if (!string.IsNullOrEmpty(source))
                {
                    context.AddSource($"{classDeclaration.Identifier.Text}_Inject.g.cs", source);
                }
            }
        }

        private string GenerateConstructorForClass(
            ClassDeclarationSyntax classDeclaration,
            SemanticModel semanticModel,
            Compilation compilation)
        {
            var namespaceName = GetNamespace(classDeclaration);
            var className = classDeclaration.Identifier.Text;

            // Get dependencies for the current class
            var injectFields = GetInjectFields(classDeclaration, semanticModel);

            // Traverse the inheritance hierarchy to collect dependencies from base classes
            var baseDependencies = new List<(string Type, string Name)>();
            var currentType = semanticModel.GetDeclaredSymbol(classDeclaration);

            if (currentType is INamedTypeSymbol currentTypeSymbol)
            {
                while (currentTypeSymbol?.BaseType != null && currentTypeSymbol.BaseType.SpecialType != SpecialType.System_Object)
                {
                    var baseTypeSyntax = currentTypeSymbol.BaseType.DeclaringSyntaxReferences
                        .FirstOrDefault()?.GetSyntax() as ClassDeclarationSyntax;

                    if (baseTypeSyntax != null)
                    {
                        baseDependencies.AddRange(GetInjectFields(baseTypeSyntax, compilation.GetSemanticModel(baseTypeSyntax.SyntaxTree)));
                    }
                    currentTypeSymbol = currentTypeSymbol.BaseType;
                }

                // Combine all dependencies
                var constructorParams = injectFields
                    .Concat(baseDependencies)
                    .Select(dep => $"{dep.Type} {dep.Name}")
                    .Distinct()
                    .ToList();

                if (!constructorParams.Any())
                    return string.Empty;

                var assignments = injectFields.Select(dep => $"this.{dep.Name} = {dep.Name};");
                var baseConstructorArgs = baseDependencies.Select(dep => dep.Name).Distinct();

                var sb = new StringBuilder();
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
                sb.AppendLine($"    public partial class {className}");
                sb.AppendLine("    {");
                sb.AppendLine($"        public {className}({string.Join(", ", constructorParams)})");
                sb.AppendLine(baseConstructorArgs.Any() ? $"            : base({string.Join(", ", baseConstructorArgs)})" : "");
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
            else
            {
                return string.Empty;
            }
        }

        private List<(string Type, string Name)> GetInjectFields(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
        {
            return classDeclaration.Members
                .OfType<FieldDeclarationSyntax>()
                .Where(field => field.AttributeLists
                    .SelectMany(attrs => attrs.Attributes)
                    .Any(attr => semanticModel.GetTypeInfo(attr).Type?.Name == "InjectAttribute"))
                .Select(field =>
                {
                    var type = field.Declaration.Type.ToString();
                    var name = field.Declaration.Variables.First().Identifier.Text;
                    return (type, name);
                })
                .ToList();
        }

        private string GetNamespace(ClassDeclarationSyntax classDeclaration)
        {
            switch (classDeclaration.Parent)
            {
                case NamespaceDeclarationSyntax nsSyntax:
                    return nsSyntax.Name.ToString();
                case FileScopedNamespaceDeclarationSyntax fsnsSyntax:
                    return fsnsSyntax.Name.ToString();
                default:
                    return "GlobalNamespace";
            }
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
