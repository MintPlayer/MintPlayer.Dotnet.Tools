using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1;

/// <summary>S1 sub-question: is GetAttributes() declaration order stable enough to carry pipeline order?</summary>
public static class AttributeOrder
{
    private const string Attrs = """
        [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
        public sealed class StepAttribute(int n) : System.Attribute { public int N { get; } = n; }
        """;

    public static void Run()
    {
        Report("single declaration, several lists",
            Attrs, "[Step(1)] [Step(2), Step(3)]\n[Step(4)] partial class C;");
        Report("partial across two trees (tree A first)",
            Attrs, "[Step(1)] [Step(2)] partial class C;", "[Step(3)] partial class C;");
        Report("partial across two trees (tree B first)",
            Attrs, "[Step(3)] partial class C;", "[Step(1)] [Step(2)] partial class C;");
    }

    private static void Report(string title, params string[] sources)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"f{i}.cs")).ToArray();
        var compilation = CSharpCompilation.Create("probe", trees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("C")!;
        var order = string.Join(",", type.GetAttributes().Select(a => a.ConstructorArguments[0].Value));
        Console.WriteLine($"  {title}: [{order}]");
    }
}
