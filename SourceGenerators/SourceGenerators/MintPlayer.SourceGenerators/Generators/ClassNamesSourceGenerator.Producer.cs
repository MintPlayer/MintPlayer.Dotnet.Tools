using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.SourceGenerators.Generators;

public class ClassNamesProducer : Producer
{
    internal const string TypeName = "ClassNames";

    private readonly IEnumerable<Models.ClassDeclaration> declarations;
    public ClassNamesProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace, "ClassNames.g.cs")
    {
        this.declarations = declarations;
    }

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        // The user owns the name: a class of their own called ClassNames in the root namespace would make this a
        // second declaration (CS0101), so nothing is generated.
        if (ClassNamesNames.DeclaresTopLevel(declarations, RootNamespace, TypeName))
            return;

        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            writer.WriteLine("/// <summary>The name of every class declared in this project, as a constant.</summary>");
            using (writer.OpenBlock($"public static class {TypeName}"))
            {
                foreach (var name in ClassNamesNames.Distinct(declarations))
                {
                    // A member cannot be named after its enclosing type (CS0542), so a class called ClassNames
                    // elsewhere gets no constant.
                    if (name == TypeName) continue;

                    writer.WriteLine($"/// <summary>The name of the <c>{name}</c> class.</summary>");
                    // @-escaped: a class declared as @event is named "event", and `const string event` does not parse.
                    writer.WriteLine($"public const string {SymbolExtensions.EscapeIdentifier(name)} = \"{name}\";");
                }
            }
        }
    }
}

public class ClassNameListProducer : Producer
{
    internal const string TypeName = "ClassNameList";

    private readonly IEnumerable<Models.ClassDeclaration> declarations;
    public ClassNameListProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace, "ClassNameList.g.cs")
    {
        this.declarations = declarations;
    }

    protected override bool WritePreamble => true;

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        // As for ClassNames: a user class of this name in the root namespace owns it.
        if (ClassNamesNames.DeclaresTopLevel(declarations, RootNamespace, TypeName))
            return;

        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            writer.WriteLine("/// <summary>The names of the classes declared in this project.</summary>");
            using (writer.OpenBlock($"public static class {TypeName}"))
            {
                var list = string.Join(", ", ClassNamesNames.Distinct(declarations).Select(name => $"\"{name}\""));

                // `new[] { }` with no elements is CS0826, "no best type found for
                // implicitly-typed array" — so this emitted uncompilable code for any
                // compilation with no matching classes. An explicitly-typed array is valid
                // whether or not there are elements.
                writer.WriteLine("/// <summary>A new array holding the name of every class declared in this project.</summary>");
                writer.WriteLine($"public static string[] List => new string[] {{ {list} }};");
            }
        }
    }
}

internal static class ClassNamesNames
{
    public static IEnumerable<string> Distinct(IEnumerable<Models.ClassDeclaration> declarations)
        => declarations.Select(d => d.Name).OfType<string>().Distinct(StringComparer.Ordinal);

    public static bool DeclaresTopLevel(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace, string typeName)
        => declarations.Any(d => d.Name == typeName && d.TopLevelNamespace == rootNamespace);
}
