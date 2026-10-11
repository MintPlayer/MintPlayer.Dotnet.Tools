using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

/// <summary>How the equality members of a model are shaped (PRD D1 to D3).</summary>
public enum EqualityShape
{
    /// <summary>A sealed class: flat <c>Equals(T?)</c>, no virtual core.</summary>
    SealedClass,
    /// <summary>A non-sealed class with no model base: exact-type check plus <c>protected virtual EqualsCore/HashCore</c>.</summary>
    HierarchyRoot,
    /// <summary>A class deriving from a model: overrides <c>EqualsCore/HashCore</c>.</summary>
    HierarchyDerived,
    /// <summary>A record with no model base. Uses the compiler's <c>EqualityContract</c>.</summary>
    Record,
    /// <summary>A record deriving from a model record: calls <c>base.Equals</c>, then compares its own properties.</summary>
    DerivedRecord,
    /// <summary>A struct: <c>Equals(S)</c> without a null path.</summary>
    Struct,
    /// <summary>A record struct: like a struct, but the compiler synthesizes <c>Equals(object)</c>.</summary>
    RecordStruct,
}

/// <summary>A type that gets generated equality members: a <c>[GenerateEquality]</c> type, or a type deriving from one.</summary>
/// <remarks>
/// Carries no symbol and no location, so it is equal across compilations whenever the generated file would be.
/// Hand-written equality, because a generator cannot generate for its own models.
/// </remarks>
public sealed class ClassDeclaration : IEquatable<ClassDeclaration>
{
    /// <summary>The containing namespace, or null for the global namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>The containing types and the type itself, re-declared by <c>OpenPathSpec(includeTarget: true)</c>.</summary>
    public PathSpec Path { get; set; } = new();

    /// <summary>The fully qualified name, with type parameters: <c>global::Ns.Box&lt;T&gt;</c>.</summary>
    public string FullName { get; set; } = string.Empty;

    public EqualityShape Shape { get; set; }

    public bool IsSealed { get; set; }

    /// <summary>For structs: the equality members can be <c>readonly</c> without an implicit copy.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>For <see cref="EqualityShape.HierarchyDerived"/>: the hierarchy root, which is the <c>EqualsCore</c> parameter type.</summary>
    public string? CoreTypeFullName { get; set; }

    /// <summary>
    /// For records: the direct base record, when there is one. A derived record casts to it for <c>base.Equals</c>;
    /// a root record with a plain base record delegates the base's members to it the same way.
    /// </summary>
    public string? BaseRecordFullName { get; set; }

    public EquatableArray<PropertyDeclaration> Properties { get; set; } = EquatableArray<PropertyDeclaration>.Empty;

    public EquatableArray<ComparerField> ComparerFields { get; set; } = EquatableArray<ComparerField>.Empty;

    /// <summary>False when the user declared <c>Equals(T)</c> (D4).</summary>
    public bool EmitEqualsT { get; set; } = true;

    /// <summary>False when the user declared <c>Equals(object)</c>, and always false for records and record structs.</summary>
    public bool EmitEqualsObject { get; set; } = true;

    /// <summary>False when the user declared <c>GetHashCode()</c>.</summary>
    public bool EmitGetHashCode { get; set; } = true;

    public bool EmitEqualsCore { get; set; } = true;

    public bool EmitHashCore { get; set; } = true;

    /// <summary>Whether to add <c>IEquatable&lt;T&gt;</c> to the base list; records get it from the compiler.</summary>
    public bool AddInterface { get; set; } = true;

    public bool Equals(ClassDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
            && Path.Equals(other.Path)
            && string.Equals(FullName, other.FullName, StringComparison.Ordinal)
            && Shape == other.Shape
            && IsSealed == other.IsSealed
            && IsReadOnly == other.IsReadOnly
            && string.Equals(CoreTypeFullName, other.CoreTypeFullName, StringComparison.Ordinal)
            && string.Equals(BaseRecordFullName, other.BaseRecordFullName, StringComparison.Ordinal)
            && Properties.Equals(other.Properties)
            && ComparerFields.Equals(other.ComparerFields)
            && EmitEqualsT == other.EmitEqualsT
            && EmitEqualsObject == other.EmitEqualsObject
            && EmitGetHashCode == other.EmitGetHashCode
            && EmitEqualsCore == other.EmitEqualsCore
            && EmitHashCore == other.EmitHashCore
            && AddInterface == other.AddInterface;
    }

    public override bool Equals(object? obj) => Equals(obj as ClassDeclaration);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, Namespace is null ? 0 : StringComparer.Ordinal.GetHashCode(Namespace));
        h = ValueEquality.Combine(h, Path.GetHashCode());
        h = ValueEquality.Combine(h, StringComparer.Ordinal.GetHashCode(FullName));
        h = ValueEquality.Combine(h, (int)Shape);
        h = ValueEquality.Combine(h, Properties.GetHashCode());
        h = ValueEquality.Combine(h, ComparerFields.GetHashCode());
        // The remaining members are all derived from the symbol that FullName names; leaving them out of the
        // hash is legal (equal values still hash equally) and keeps this short.
        return h;
    }

    public override string ToString() => FullName;
}
