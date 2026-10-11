using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>One <c>[GenericMethod]</c> method that passed every check: everything the producer writes, as plain values.</summary>
[GenerateEquality]
public partial class GenericMethodDeclaration
{
    /// <summary>The containing type, re-declared by <c>OpenPathSpec(includeTarget: true)</c>.</summary>
    public PathSpec? PathSpec { get; set; }

    /// <summary>The method name as written in code: <c>@</c>-escaped when it is a keyword.</summary>
    public string? MethodName { get; set; }

    public bool IsStatic { get; set; }

    /// <summary>The fully qualified return type, nullable annotations included; null for <c>void</c>.</summary>
    public string? ReturnType { get; set; }

    /// <summary>
    /// The collection's elements are non-nullable <c>object</c>, so each type parameter gets <c>where TArg : notnull</c>;
    /// otherwise <c>[t1, t2]</c> is CS8601 under <c>#nullable enable</c>.
    /// </summary>
    public bool ElementsNotNull { get; set; }

    /// <summary>The overloads' type parameters are this prefix plus 1..n, free of every type parameter in scope.</summary>
    public string? TypeParameterPrefix { get; set; }

    /// <summary>The highest arity, 2..16.</summary>
    public int Count { get; set; }
}
