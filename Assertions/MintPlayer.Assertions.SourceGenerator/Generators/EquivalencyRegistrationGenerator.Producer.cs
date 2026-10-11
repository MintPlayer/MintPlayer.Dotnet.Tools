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

    /// <summary>
    /// Emits one registration per type: the default members, and — only when there are any — the
    /// members an option has to ask for.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The two arrays stay two arrays.</b> The default table is what the walker is handed on
    /// every ordinary comparison, and it must contain exactly the members it contained before traits
    /// existed: <c>FindByName</c> is O(members²) per node, so folding a type's internal members into
    /// the default table would slow down every suite that never asked for them. The second call is
    /// omitted entirely when a type has nothing excluded, which is almost every type — emitting an
    /// empty one would cost a dictionary entry per type at module-initialiser time for nothing.
    /// </remarks>
    private static void WriteRegistration(IndentedTextWriter writer, EquivalencyTypeDeclaration type)
    {
        var defaults = new List<MemberDeclaration>();
        var extended = new List<MemberDeclaration>();
        foreach (var member in type.Members)
        {
            (member.IsExcludedByDefault ? extended : defaults).Add(member);
        }

        WriteAccessorArray(writer, $"{RegistryType}.RegisterAccessors(typeof({type.TypeFullName})", type, defaults, suffix: ");");

        if (extended.Count > 0 || !type.ExtendedMembersComplete)
        {
            WriteAccessorArray(writer, $"{RegistryType}.RegisterExtendedAccessors(typeof({type.TypeFullName})", type, extended,
                suffix: $", {(type.ExtendedMembersComplete ? "true" : "false")});");
        }

        writer.WriteLine();
    }

    private static void WriteAccessorArray(IndentedTextWriter writer, string callPrefix,
        EquivalencyTypeDeclaration type, List<MemberDeclaration> members, string suffix)
    {
        if (members.Count == 0)
        {
            writer.WriteLine($"{callPrefix}, new {AccessorType}[0]{suffix}");
            return;
        }

        writer.WriteLine($"{callPrefix}, new {AccessorType}[]");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var member in members)
        {
            writer.WriteLine($"new {AccessorType}(\"{member.Name}\", typeof({member.TypeFullName}), static o => (({type.TypeFullName})o).{Escape(member.Name)}, {TraitsExpression(member)}),");
        }
        writer.Indent--;
        writer.WriteLine($"}}{suffix}");
    }

    /// <summary>
    /// Renders the traits as a <c>MemberTraits</c> expression. Written out as named flags rather
    /// than a cast integer so the generated file stays readable and a drift between the generator's
    /// <see cref="MemberTraitFlags"/> and the runtime enum fails to compile instead of silently
    /// producing the wrong member set.
    /// </summary>
    private static string TraitsExpression(MemberDeclaration member)
    {
        var parts = new List<string>();
        foreach (var flag in new[]
        {
            MemberTraitFlags.Property, MemberTraitFlags.Field, MemberTraitFlags.NonPublic,
            MemberTraitFlags.NonBrowsable, MemberTraitFlags.ExplicitInterface,
        })
        {
            if ((member.Traits & flag) != 0) parts.Add($"{TraitsType}.{flag}");
        }

        return parts.Count == 0 ? $"{TraitsType}.None" : string.Join(" | ", parts);
    }

    /// <summary>Member names are raw identifiers; a name that happens to be a keyword needs the verbatim prefix.</summary>
    private static string Escape(string identifier)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(identifier) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? identifier
            : "@" + identifier;
}
