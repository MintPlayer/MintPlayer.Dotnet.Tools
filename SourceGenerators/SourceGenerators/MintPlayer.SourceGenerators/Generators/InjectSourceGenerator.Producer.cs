using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

internal class InjectProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos;
    public InjectProducer(IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos, string rootNamespace) : base(rootNamespace, $"Inject.g.cs")
    {
        this.classInfos = classInfos;
    }
    public InjectProducer(IEnumerable<Models.ClassWithBaseDependenciesAndInjectFields> classInfos, string rootNamespace, string filename) : base(rootNamespace, filename)
    {
        this.classInfos = classInfos;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        return classInfos
            .SelectMany(ci => ci.Diagnostics)
            .Select(d => d.Rule.Create(d.Location?.ToLocation(compilation), d.MessageArgs));
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var classInfoNamespace in classInfos.GroupBy(ci => ci.PathSpec?.ContainingNamespace ?? ci.ClassNamespace ?? RootNamespace))
        {
            IDisposableWriterIndent? namespaceBlock = string.IsNullOrEmpty(classInfoNamespace.Key) ? null : writer.OpenBlock($"namespace {classInfoNamespace.Key}");

            foreach (var classInfo in classInfoNamespace)
            {
                var constructorParams = classInfo.InjectFields
                    .Concat(classInfo.BaseDependencies)
                    .Select(dep => $"{dep.Type} {dep.Name}")
                    .Distinct()
                    .ToList();

                if (!constructorParams.Any()) continue;

                var assignments = classInfo.InjectFields.Select(dep => $"this.{dep.Name} = {dep.Name};");
                var baseConstructorArgs = classInfo.BaseDependencies.Select(dep => dep.Name).Distinct();

                using (writer.OpenPathSpec(classInfo.PathSpec))
                using (writer.OpenBlock($"partial class {classInfo.ClassName}"))
                {
                    writer.WriteLine($"public {classInfo.ClassName}({string.Join(", ", constructorParams)})");
                    if (baseConstructorArgs.Any())
                        writer.IndentSingleLine(baseConstructorArgs.Any() ? $": base({string.Join(", ", baseConstructorArgs)})" : string.Empty);

                    using (writer.OpenBlock(string.Empty))
                    {
                        foreach (var assignment in assignments)
                            writer.WriteLine(assignment);

                        if (!string.IsNullOrEmpty(classInfo.PostConstructMethodName))
                            writer.WriteLine($"{classInfo.PostConstructMethodName}();");
                    }
                }
            }

            namespaceBlock?.Dispose();
        }
    }
}
