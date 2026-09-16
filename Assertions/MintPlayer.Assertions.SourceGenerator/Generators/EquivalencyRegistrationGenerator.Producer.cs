using MintPlayer.Assertions.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.Assertions.SourceGenerator.Generators;

internal sealed class EquivalencyRegistrationProducer : Producer
{
    private const string RegistryType = "global::MintPlayer.Assertions.Equivalency.EquivalencyRegistry";
    private const string AccessorType = "global::MintPlayer.Assertions.Equivalency.MemberAccessor";
    private const string TraitsType = "global::MintPlayer.Assertions.Equivalency.MemberTraits";

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
            // An explicit interface implementation is unreachable through the concrete type, so the
            // getter casts to the interface that declares it.
            var castTo = member.DeclaringInterfaceFullName ?? type.TypeFullName;
            writer.WriteLine($"new {AccessorType}(\"{member.Name}\", typeof({member.TypeFullName}), static o => (({castTo})o).{Escape(member.Name)}, {TraitsExpression(member.Traits)}),");
        }
        writer.Indent--;
        writer.WriteLine("});");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the traits as an or-ed list of named enum values rather than a cast integer, so the
    /// generated file says what it means and a mismatch with the runtime enum is a compile error
    /// instead of a silently wrong flag.
    /// </summary>
    private static string TraitsExpression(MemberTraitsValue traits)
    {
        if (traits == MemberTraitsValue.None) return $"{TraitsType}.None";

        var parts = new List<string>(4);
        if ((traits & MemberTraitsValue.Field) != 0) parts.Add($"{TraitsType}.Field");
        if ((traits & MemberTraitsValue.NonPublic) != 0) parts.Add($"{TraitsType}.NonPublic");
        if ((traits & MemberTraitsValue.NonBrowsable) != 0) parts.Add($"{TraitsType}.NonBrowsable");
        if ((traits & MemberTraitsValue.ExplicitInterface) != 0) parts.Add($"{TraitsType}.ExplicitInterface");
        return string.Join(" | ", parts);
    }

    /// <summary>Member names are raw identifiers; a name that happens to be a keyword needs the verbatim prefix.</summary>
    private static string Escape(string identifier)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(identifier) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? identifier
            : "@" + identifier;
}
