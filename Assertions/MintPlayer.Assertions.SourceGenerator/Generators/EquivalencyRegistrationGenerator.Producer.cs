using MintPlayer.Assertions.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.Assertions.SourceGenerator.Generators;

internal sealed class EquivalencyRegistrationProducer : Producer
{
    private const string RegistryType = "global::MintPlayer.Assertions.Equivalency.EquivalencyRegistry";
    private const string AccessorType = "global::MintPlayer.Assertions.Equivalency.MemberAccessor";

    private readonly EquatableArray<EquivalencyTypeDeclaration> types;
    private readonly bool hasRuntimeReference;

    public EquivalencyRegistrationProducer(EquatableArray<EquivalencyTypeDeclaration> types, bool hasRuntimeReference, string? rootNamespace)
        : base(string.IsNullOrWhiteSpace(rootNamespace) ? "MintPlayer.Assertions.Generated" : rootNamespace!, "AssertionsEquivalencyRegistrations.g.cs")
    {
        this.types = types;
        this.hasRuntimeReference = hasRuntimeReference;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        if (!hasRuntimeReference || types.Count == 0) return;

        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine(Header);
        writer.WriteLine();

        using (writer.OpenBlock($"namespace {RootNamespace}"))
        {
            writer.WriteLine("/// <summary>Registers source-generated equivalency accessors, so BeEquivalentTo never needs reflection for these types.</summary>");
            writer.WriteLine("[global::System.CodeDom.Compiler.GeneratedCode(\"MintPlayer.Assertions.SourceGenerator\", \"1.0.0\")]");
            using (writer.OpenBlock("internal static class AssertionsEquivalencyRegistrations"))
            {
                writer.WriteLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
                using (writer.OpenBlock("internal static void RegisterEquivalencyAccessors()"))
                {
                    foreach (var type in types)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        WriteRegistration(writer, type);
                    }
                }
            }
        }
    }

    private static void WriteRegistration(IndentedTextWriter writer, EquivalencyTypeDeclaration type)
    {
        if (type.Members.Count == 0)
        {
            writer.WriteLine($"{RegistryType}.RegisterAccessors(typeof({type.TypeFullName}), new {AccessorType}[0]);");
            writer.WriteLine();
            return;
        }

        writer.WriteLine($"{RegistryType}.RegisterAccessors(typeof({type.TypeFullName}), new {AccessorType}[]");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var member in type.Members)
        {
            writer.WriteLine($"new {AccessorType}(\"{member.Name}\", typeof({member.TypeFullName}), static o => (({type.TypeFullName})o).{Escape(member.Name)}, {(member.IsProperty ? "true" : "false")}),");
        }
        writer.Indent--;
        writer.WriteLine("});");
        writer.WriteLine();
    }

    /// <summary>Member names are raw identifiers; a name that happens to be a keyword needs the verbatim prefix.</summary>
    private static string Escape(string identifier)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(identifier) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? identifier
            : "@" + identifier;
}
