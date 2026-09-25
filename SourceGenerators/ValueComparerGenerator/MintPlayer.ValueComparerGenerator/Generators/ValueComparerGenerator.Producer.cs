using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;
using System.CodeDom.Compiler;

namespace MintPlayer.ValueComparerGenerator.Producers;

/// <summary>Emits the equality members of one model into <c>&lt;Type&gt;.Equality.g.cs</c> (PRD D1 to D4).</summary>
/// <remarks>
/// Every comparison was chosen at discovery time from the property's type symbol; this only lays out the members for
/// the model's <see cref="EqualityShape"/> and substitutes the operands into each property's templates.
/// </remarks>
public sealed class EqualityProducer : Producer
{
    private const string ObjectReferenceEquals = "global::System.Object.ReferenceEquals";

    // Locals and parameters of the generated members; a property with one of these names is qualified with this.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal) { "other", "obj", "h", "o" };

    private readonly ClassDeclaration model;

    public EqualityProducer(ClassDeclaration model) : base(model.Namespace ?? string.Empty, model.HintName)
    {
        this.model = model;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine(Header);
        writer.WriteLine();

        var blocks = new Stack<IDisposable>();
        if (model.Namespace is not null)
            blocks.Push(writer.OpenBlock($"namespace {model.Namespace}"));
        foreach (var parent in model.Parents)
            blocks.Push(writer.OpenBlock($"partial {parent.Keyword} {parent.Name}"));

        var baseList = model.AddInterface ? $" : global::System.IEquatable<{model.FullName}>" : string.Empty;
        using (writer.OpenBlock($"partial {model.Keyword} {model.Name}{baseList}"))
        {
            foreach (var field in model.ComparerFields)
                writer.WriteLine($"private static readonly {field.Type} {field.Name} = {field.Initializer};");
            if (model.ComparerFields.Count > 0)
                writer.WriteLine();

            switch (model.Shape)
            {
                case EqualityShape.SealedClass: WriteSealedClass(writer); break;
                case EqualityShape.HierarchyRoot: WriteHierarchyRoot(writer); break;
                case EqualityShape.HierarchyDerived: WriteHierarchyDerived(writer); break;
                case EqualityShape.Record: WriteRecord(writer); break;
                case EqualityShape.DerivedRecord: WriteDerivedRecord(writer); break;
                case EqualityShape.Struct:
                case EqualityShape.RecordStruct: WriteStruct(writer); break;
            }
        }

        while (blocks.Count > 0)
            blocks.Pop().Dispose();
    }

    private void WriteSealedClass(IndentedTextWriter writer)
    {
        if (model.EmitEqualsT)
        {
            using (writer.OpenBlock($"public bool Equals({model.FullName}? other)"))
            {
                writer.WriteLine("if (other is null) return false;");
                writer.WriteLine($"if ({ObjectReferenceEquals}(this, other)) return true;");
                WriteReturnEquals(writer, "other", prefix: null);
            }
            writer.WriteLine();
        }

        WriteEqualsObject(writer);

        if (model.EmitGetHashCode)
            WriteHash(writer, "public override int GetHashCode()", seed: "17");
    }

    private void WriteHierarchyRoot(IndentedTextWriter writer)
    {
        if (model.EmitEqualsT)
        {
            using (writer.OpenBlock($"public bool Equals({model.FullName}? other)"))
            {
                writer.WriteLine("if (other is null) return false;");
                writer.WriteLine($"if ({ObjectReferenceEquals}(this, other)) return true;");
                writer.WriteLine("return other.GetType() == GetType() && EqualsCore(other);");
            }
            writer.WriteLine();
        }

        WriteEqualsObject(writer);

        if (model.EmitGetHashCode)
        {
            writer.WriteLine("public override int GetHashCode() => HashCore();");
            writer.WriteLine();
        }

        if (model.EmitEqualsCore)
        {
            using (writer.OpenBlock($"protected virtual bool EqualsCore({model.FullName} other)"))
                WriteReturnEquals(writer, "other", prefix: null);
            writer.WriteLine();
        }

        if (model.EmitHashCore)
            WriteHash(writer, "protected virtual int HashCore()", seed: "17");
    }

    private void WriteHierarchyDerived(IndentedTextWriter writer)
    {
        if (model.EmitEqualsT)
        {
            writer.WriteLine($"public bool Equals({model.FullName}? other) => base.Equals(other);");
            writer.WriteLine();
        }

        if (model.EmitEqualsCore)
        {
            using (writer.OpenBlock($"protected override bool EqualsCore({model.CoreTypeFullName} other)"))
            {
                if (Compared.Any())
                {
                    writer.WriteLine("if (!base.EqualsCore(other)) return false;");
                    writer.WriteLine($"var o = ({model.FullName})other;");
                    WriteReturnEquals(writer, "o", prefix: null);
                }
                else
                {
                    WriteIgnoredComments(writer);
                    writer.WriteLine("return base.EqualsCore(other);");
                }
            }
            writer.WriteLine();
        }

        if (model.EmitHashCore)
            WriteHash(writer, "protected override int HashCore()", seed: "base.HashCore()");
    }

    private void WriteRecord(IndentedTextWriter writer)
    {
        if (model.EmitEqualsT)
        {
            using (writer.OpenBlock($"public {(model.IsSealed ? "" : "virtual ")}bool Equals({model.FullName}? other)"))
            {
                writer.WriteLine("if (other is null) return false;");
                writer.WriteLine($"if ({ObjectReferenceEquals}(this, other)) return true;");
                // A plain base record compares its own members, EqualityContract included.
                WriteReturnEquals(writer, "other", prefix: model.BaseRecordFullName is { } baseRecord
                    ? $"base.Equals(({baseRecord}?)other)"
                    : "EqualityContract == other.EqualityContract");
            }
            writer.WriteLine();
        }

        if (model.EmitGetHashCode)
            WriteHash(writer, "public override int GetHashCode()", seed: model.BaseRecordFullName is null
                ? "global::System.Collections.Generic.EqualityComparer<global::System.Type>.Default.GetHashCode(EqualityContract)"
                : "base.GetHashCode()");
    }

    private void WriteDerivedRecord(IndentedTextWriter writer)
    {
        if (model.EmitEqualsT)
        {
            using (writer.OpenBlock($"public {(model.IsSealed ? "" : "virtual ")}bool Equals({model.FullName}? other)"))
            {
                writer.WriteLine($"if ({ObjectReferenceEquals}(this, other)) return true;");
                WriteReturnEquals(writer, "other", prefix: $"other is not null && base.Equals(({model.BaseRecordFullName}?)other)");
            }
            writer.WriteLine();
        }

        if (model.EmitGetHashCode)
            WriteHash(writer, "public override int GetHashCode()", seed: "base.GetHashCode()");
    }

    private void WriteStruct(IndentedTextWriter writer)
    {
        var readOnly = model.IsReadOnly ? "readonly " : string.Empty;

        if (model.EmitEqualsT)
        {
            using (writer.OpenBlock($"public {readOnly}bool Equals({model.FullName} other)"))
                WriteReturnEquals(writer, "other", prefix: null);
            writer.WriteLine();
        }

        if (model.Shape == EqualityShape.Struct && model.EmitEqualsObject)
        {
            writer.WriteLine($"public override {readOnly}bool Equals(object? obj) => obj is {model.FullName} other && Equals(other);");
            writer.WriteLine();
        }

        if (model.EmitGetHashCode)
            WriteHash(writer, $"public override {readOnly}int GetHashCode()", seed: "17");
    }

    private void WriteEqualsObject(IndentedTextWriter writer)
    {
        if (!model.EmitEqualsObject) return;
        writer.WriteLine($"public override bool Equals(object? obj) => Equals(obj as {model.FullName});");
        writer.WriteLine();
    }

    private IEnumerable<PropertyDeclaration> Compared => model.Properties.Where(p => !p.HasEqualityIgnore);

    private void WriteIgnoredComments(IndentedTextWriter writer)
    {
        foreach (var property in model.Properties.Where(p => p.HasEqualityIgnore))
            writer.WriteLine($"// {property.Name}: [EqualityIgnore]");
    }

    /// <summary>Writes <c>return prefix &amp;&amp; p1 &amp;&amp; p2 ...;</c>, one term per line.</summary>
    private void WriteReturnEquals(IndentedTextWriter writer, string other, string? prefix)
    {
        WriteIgnoredComments(writer);

        var terms = new List<string>();
        if (prefix is not null) terms.Add(prefix);
        terms.AddRange(Compared.Select(p => p.EqualsExpression
            .Replace("$L$", Self(p.Name))
            .Replace("$R$", $"{other}.{p.Name}")));

        if (terms.Count == 0)
        {
            writer.WriteLine("return true;");
            return;
        }

        writer.Write("return ");
        writer.Write(terms[0]);
        writer.Indent++;
        foreach (var term in terms.Skip(1))
        {
            writer.WriteLine();
            writer.Write("&& ");
            writer.Write(term);
        }
        writer.WriteLine(";");
        writer.Indent--;
    }

    /// <summary>Hashes exactly the properties <see cref="WriteReturnEquals"/> compares, so the two always agree.</summary>
    private void WriteHash(IndentedTextWriter writer, string signature, string seed)
    {
        using (writer.OpenBlock(signature))
        using (writer.OpenBlock("unchecked"))
        {
            writer.WriteLine($"int h = {seed};");
            foreach (var property in Compared)
                writer.WriteLine($"h = h * 31 + {Parenthesize(property.HashExpression.Replace("$V$", Self(property.Name)))};");
            writer.WriteLine("return h;");
        }
    }

    private static string Self(string name) => ReservedNames.Contains(name) ? "this." + name : name;

    /// <summary>
    /// Wraps the expression in parentheses unless it is already a single operand: a call, a member access or a
    /// parenthesized group, recognised by having no space outside parentheses and generic brackets.
    /// </summary>
    private static string Parenthesize(string expression)
    {
        var depth = 0;
        foreach (var c in expression)
        {
            if (c is '(' or '<') depth++;
            else if (c is ')' or '>') depth--;
            else if (c == ' ' && depth == 0) return $"({expression})";
        }
        return expression;
    }
}
