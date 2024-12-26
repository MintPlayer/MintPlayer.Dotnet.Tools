using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;
using System.Linq;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    internal class InjectProducer : Producer
    {
        private readonly Models.ClassWithBaseDependenciesAndInjectFields classInfo;
        public InjectProducer(Models.ClassWithBaseDependenciesAndInjectFields classInfo, string rootNamespace, string filename) : base(rootNamespace, filename)
        {
            this.classInfo = classInfo;
        }

        protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
        {
            var constructorParams = classInfo.InjectFields
                .Concat(classInfo.BaseDependencies)
                .Select(dep => $"{dep.Type} {dep.Name}")
                .Distinct()
                .ToList();

            if (!constructorParams.Any()) return;

            var assignments = classInfo.InjectFields.Select(dep => $"this.{dep.Name} = {dep.Name};");
            var baseConstructorArgs = classInfo.BaseDependencies.Select(dep => dep.Name).Distinct();


            writer.WriteLine($"namespace {classInfo.ClassNamespace ?? RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;

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

            writer.Indent--;
            writer.WriteLine("}");
        }
    }
}
