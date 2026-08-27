using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>Which assertions class the generated extension method hangs off.</summary>
public enum SubjectKind
{
    Object,
    String,
    Boolean,
    Numeric,
}

/// <summary>One extra parameter of the user's predicate, forwarded to the generated assertion.</summary>
public sealed class ParameterDeclaration : IEquatable<ParameterDeclaration>
{
    public ParameterDeclaration(string name, string typeFullName)
    {
        Name = name;
        TypeFullName = typeFullName;
    }

    public string Name { get; }
    public string TypeFullName { get; }

    public bool Equals(ParameterDeclaration? other)
        => other is not null && Name == other.Name && TypeFullName == other.TypeFullName;

    public override bool Equals(object? obj) => Equals(obj as ParameterDeclaration);

    public override int GetHashCode()
    {
        unchecked { return Name.GetHashCode() * 31 + TypeFullName.GetHashCode(); }
    }
}

/// <summary>
/// A <c>[GenerateAssertion]</c> predicate reduced to everything the emitter needs, plus the
/// reason it was rejected when the shape is unsupported (in which case only
/// <see cref="Diagnostic"/> and <see cref="Location"/> are meaningful).
/// </summary>
public sealed class AssertionMethodDeclaration : IEquatable<AssertionMethodDeclaration>
{
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Simple name of the class declaring the predicate (nested classes joined with '_').</summary>
    public string ContainingTypeName { get; set; } = string.Empty;

    /// <summary>Fully qualified (global::) name of the class declaring the predicate.</summary>
    public string ContainingTypeFullName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    /// <summary>Name of the generated fluent assertion (from the attribute, or derived).</summary>
    public string GeneratedName { get; set; } = string.Empty;

    /// <summary>The generated name humanized for the failure message, e.g. "be even".</summary>
    public string Phrase { get; set; } = string.Empty;

    public SubjectKind SubjectKind { get; set; }

    /// <summary>Fully qualified subject type; for numerics the non-nullable underlying type.</summary>
    public string SubjectTypeFullName { get; set; } = string.Empty;

    public EquatableArray<ParameterDeclaration> ExtraParameters { get; set; } = EquatableArray<ParameterDeclaration>.Empty;

    /// <summary>
    /// False when the predicate's class or one of its parameter types is not public — the
    /// generated extension class then has to be internal to keep accessibility consistent.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>Set when the method shape is unsupported; the emitter skips it and MPAG001 is reported.</summary>
    public string? Diagnostic { get; set; }

    public LocationKey? Location { get; set; }

    public bool Equals(AssertionMethodDeclaration? other)
        => other is not null
        && Namespace == other.Namespace
        && ContainingTypeName == other.ContainingTypeName
        && ContainingTypeFullName == other.ContainingTypeFullName
        && MethodName == other.MethodName
        && GeneratedName == other.GeneratedName
        && Phrase == other.Phrase
        && SubjectKind == other.SubjectKind
        && SubjectTypeFullName == other.SubjectTypeFullName
        && ExtraParameters.Equals(other.ExtraParameters)
        && IsPublic == other.IsPublic
        && Diagnostic == other.Diagnostic
        && LocationEquals(Location, other.Location);

    private static bool LocationEquals(LocationKey? a, LocationKey? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.FilePath == b.FilePath && a.StartLine == b.StartLine && a.StartColumn == b.StartColumn
            && a.EndLine == b.EndLine && a.EndColumn == b.EndColumn;
    }

    public override bool Equals(object? obj) => Equals(obj as AssertionMethodDeclaration);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = ContainingTypeFullName.GetHashCode();
            hash = hash * 31 + MethodName.GetHashCode();
            hash = hash * 31 + GeneratedName.GetHashCode();
            hash = hash * 31 + (int)SubjectKind;
            hash = hash * 31 + SubjectTypeFullName.GetHashCode();
            hash = hash * 31 + ExtraParameters.GetHashCode();
            hash = hash * 31 + (Diagnostic?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public override string ToString() => $"{ContainingTypeFullName}.{MethodName}";
}
