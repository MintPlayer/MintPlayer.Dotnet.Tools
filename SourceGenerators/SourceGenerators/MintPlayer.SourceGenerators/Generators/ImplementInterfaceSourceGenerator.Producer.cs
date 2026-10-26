using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

public class ImplementInterfaceProducer : Producer
{
    public ImplementInterfaceProducer(IEnumerable<Models.ImplementInterfaceDeclaration> types, string rootNamespace) : base(rootNamespace, "ImplementInterface.g.cs")
    {
        Types = types;
    }

    public IEnumerable<Models.ImplementInterfaceDeclaration> Types { get; }

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        var firstType = true;
        foreach (var type in Types.Where(t => t.PathSpec is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstType) writer.WriteLine();
            firstType = false;

            var baseList = type.BaseList.Length == 0 ? null : " : " + string.Join(", ", type.BaseList);
            using (writer.OpenNamespace(type.PathSpec!.ContainingNamespace))
            using (writer.OpenPathSpec(type.PathSpec, includeTarget: true, baseList: baseList))
            {
                foreach (var member in type.Members)
                    foreach (var line in member.Split('\n'))
                    {
                        // A public member takes the interface member's documentation; without it a consumer with
                        // GenerateDocumentationFile gets CS1591 on code it cannot document. Explicit ones are not public.
                        if (line.StartsWith("public ", StringComparison.Ordinal))
                            writer.WriteLine("/// <inheritdoc />");
                        writer.WriteLine(line);
                    }
            }
        }
    }
}
