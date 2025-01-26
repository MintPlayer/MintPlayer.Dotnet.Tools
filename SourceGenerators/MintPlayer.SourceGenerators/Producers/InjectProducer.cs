using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers;

internal class InjectProducer : Producer
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

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var classInfoNamespace in classInfos.GroupBy(ci => ci.ClassNamespace ?? RootNamespace))
        {
            writer.WriteLine($"namespace {classInfoNamespace.Key}");
            writer.WriteLine("{");
            writer.Indent++;

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

                writer.WriteLine($"public partial class {classInfo.ClassName}");
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
