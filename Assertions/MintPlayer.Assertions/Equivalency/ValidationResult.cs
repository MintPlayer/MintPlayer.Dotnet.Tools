namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// A structural node at which the comparison asserted nothing, so it could not have failed.
/// </summary>
/// <param name="Path">
/// The path into the object graph where it happened (empty for the root), so a vacuous node
/// nested inside an otherwise-meaningful comparison can be pointed at precisely.
/// </param>
/// <param name="ExpectationType">The type whose members drove (or failed to drive) the comparison.</param>
/// <param name="SubjectType">The subject's runtime type at that node.</param>
/// <param name="ExpectationHasNoMembers">
/// True when the expectation type exposes no comparable members at all, false when it has members
/// but every one of them was removed by the configured exclusions or inclusions. The two causes
/// have different cures, so they get different messages.
/// </param>
internal sealed record VacuousNode(
    string Path,
    Type ExpectationType,
    Type SubjectType,
    bool ExpectationHasNoMembers);

/// <summary>
/// The outcome of one equivalency validation: the differences found, plus whether the comparison
/// was <em>vacuous</em> — i.e. some structural node compared nothing at all.
/// </summary>
/// <remarks>
/// The distinction matters because <c>BeEquivalentTo</c> is expectation-driven: it walks the
/// expectation's members and ignores extra subject members, which is what makes comparing a full
/// DTO against an anonymous object holding only the interesting members work. The degenerate
/// consequence is that an expectation contributing zero members yields zero differences, so the
/// positive assertion passes unconditionally and the negative one fails unconditionally. Such an
/// assertion reads as a green regression fence over a code path nobody is checking, so the engine
/// reports it and the assertion methods turn it into a loud error instead of a silent pass.
/// </remarks>
/// <param name="Differences">Every structural difference found; empty when equivalent.</param>
/// <param name="Vacuity">The first node that compared nothing, or null when the comparison was meaningful.</param>
internal sealed record ValidationResult(List<Difference> Differences, VacuousNode? Vacuity)
{
    /// <summary>True when some node compared nothing, making the result meaningless in both directions.</summary>
    public bool IsVacuous => Vacuity is not null;
}
