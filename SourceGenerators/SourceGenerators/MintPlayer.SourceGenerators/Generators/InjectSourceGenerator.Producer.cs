using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

internal class InjectProducer : Producer
{
    private readonly IEnumerable<Models.ClassesByNamespace> classInfos;
    public InjectProducer(IEnumerable<Models.ClassesByNamespace> classInfos, string rootNamespace) : base(rootNamespace, $"Inject.g.cs")
    {
        this.classInfos = classInfos;
    }
    public InjectProducer(IEnumerable<Models.ClassesByNamespace> classInfos, string rootNamespace, string filename) : base(rootNamespace, filename)
    {
        this.classInfos = classInfos;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var classInfoNamespace in classInfos)
        {
            writer.WriteLine($"namespace {classInfoNamespace.Namespace ?? RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var classInfo in classInfoNamespace.Classes)
            {
                var constructorParams = classInfo.InjectFields
                    .Concat(classInfo.BaseDependencies)
                    .Select(dep => $"{dep.Type} {dep.Name}")
                    .Distinct()
                    .ToList();

                if (!constructorParams.Any()) continue;

                var assignments = classInfo.InjectFields.Select(dep => $"this.{dep.Name} = {dep.Name};");
                var baseConstructorArgs = classInfo.BaseDependencies.Select(dep => dep.Name).Distinct();

                writer.WriteLine($"partial class {classInfo.ClassName}");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine($"public {classInfo.ClassName}({string.Join(", ", constructorParams)})");
                if (baseConstructorArgs.Any())
                {
                    writer.Indent++;
                    writer.WriteLine(baseConstructorArgs.Any() ? $": base({string.Join(", ", baseConstructorArgs)})" : "");
                    writer.Indent--;
                }

                writer.WriteLine("{");
                writer.Indent++;

                foreach (var assignment in assignments)
                    writer.WriteLine(assignment);

                writer.Indent--;
                writer.WriteLine("}");

                writer.Indent--;
                writer.WriteLine("}");
            }

            writer.Indent--;
            writer.WriteLine("}");
        }
    }
}
