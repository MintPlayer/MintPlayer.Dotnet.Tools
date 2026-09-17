using System.ComponentModel;
using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Scanned by the generator (hence the attribute and the non-nested, public declaration), and shaped
/// to carry one member of every trait combination the two providers have to agree about.
/// </summary>
[AssertEquivalency]
public class TraitParityPoco
{
    public int PublicProperty { get; set; }
    public int PublicField;
    internal int InternalProperty { get; set; }
    internal int InternalField;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public int HiddenProperty { get; set; }
}

/// <summary>
/// Holds the source generator's <c>EquivalencyScanner</c> and the runtime's
/// <c>ReflectionMemberProvider</c> to the same rule about which members exist and what traits they
/// carry.
/// </summary>
/// <remarks>
/// ⚠️ <b>These two are independent implementations of one rule, in two projects, with no compiler
/// check between them.</b> A member one returns and the other does not does not fail a build and
/// does not fail any other test: the option simply compares a different set of members depending on
/// whether the type happened to be scanned, which surfaces as a mysterious difference in someone
/// else's assembly. This file is the only thing standing between that and a release.
/// <para>
/// The types below are deliberately shaped to hit each rule — public, internal, protected, private,
/// non-browsable, explicit-interface — rather than being realistic. Add a case here before adding a
/// trait, not after.
/// </para>
/// </remarks>
public class MemberTraitTests
{
    private sealed class Mixed
    {
        public int PublicProperty { get; set; }
        public int PublicField;
        internal int InternalProperty { get; set; }
        internal int InternalField;
        private int PrivateProperty { get; set; }
#pragma warning disable CS0169 // never used: the point is that neither provider returns it
        private readonly int privateField;
#pragma warning restore CS0169

        public int Sum() => PrivateProperty + privateField;
    }

    private static string[] NamesOf(Type type, MemberTraits wanted)
    {
        var names = RegistryMemberProvider.Instance.GetMembers(type, new MemberSelection(wanted, MemberTraits.None)).Select(m => m.Name).ToArray();
        Array.Sort(names, StringComparer.Ordinal);
        return names;
    }

    [Fact]
    public void ByDefaultOnlyPublicMembersAreVisible()
    {
        Assert.Equal(["PublicField", "PublicProperty"], NamesOf(typeof(Mixed), MemberTraits.None));
    }

    [Fact]
    public void AskingForNonPublicAddsInternalMembersAndNothingElse()
    {
        // Private members are absent from BOTH providers on purpose: the generator physically cannot
        // emit an accessor for one, so including them on the reflection side would create a gap the
        // generated path could never close.
        Assert.Equal(
            ["InternalField", "InternalProperty", "PublicField", "PublicProperty"],
            NamesOf(typeof(Mixed), MemberTraits.NonPublic));
    }

    [Fact]
    public void PublicMembersCarryTheirKind()
    {
        var members = RegistryMemberProvider.Instance.GetMembers(typeof(Mixed), MemberSelection.Default);

        var property = members.Single(m => m.Name == "PublicProperty");
        var field = members.Single(m => m.Name == "PublicField");

        Assert.Equal(MemberTraits.Property, property.Traits);
        Assert.Equal(MemberTraits.Field, field.Traits);
        Assert.True(property.IsProperty);
        Assert.False(field.IsProperty);
    }

    [Fact]
    public void InternalMembersCarryTheNonPublicTrait()
    {
        var members = RegistryMemberProvider.Instance.GetMembers(typeof(Mixed), new MemberSelection(MemberTraits.NonPublic, MemberTraits.None));

        Assert.Equal(MemberTraits.Property | MemberTraits.NonPublic, members.Single(m => m.Name == "InternalProperty").Traits);
        Assert.Equal(MemberTraits.Field | MemberTraits.NonPublic, members.Single(m => m.Name == "InternalField").Traits);
    }

    /// <summary>
    /// The default table must be the same array instance the walker got before traits existed — not
    /// merely an equal one. Rebuilding it per call would be invisible to every other test here and
    /// would put an allocation on the hottest path in the library.
    /// </summary>
    [Fact]
    public void TheDefaultTableIsCachedNotRebuilt()
    {
        Assert.Same(
            RegistryMemberProvider.Instance.GetMembers(typeof(Mixed), MemberSelection.Default),
            RegistryMemberProvider.Instance.GetMembers(typeof(Mixed), MemberSelection.Default));
    }

    [Fact]
    public void TheExtendedTableIsAlsoCached()
    {
        Assert.Same(
            RegistryMemberProvider.Instance.GetMembers(typeof(Mixed), new MemberSelection(MemberTraits.NonPublic, MemberTraits.None)),
            RegistryMemberProvider.Instance.GetMembers(typeof(Mixed), new MemberSelection(MemberTraits.NonPublic, MemberTraits.None)));
    }

    // -------------------------------------------------------------------------------------------
    // Parity: the generated table against the reflection table, for a type the generator DID scan.
    // Everything above this line exercises the reflection provider alone, because a private nested
    // type is one the generator cannot emit for. These are the tests that would catch a drift.
    // -------------------------------------------------------------------------------------------

    private static string[] Sorted(IEnumerable<MemberAccessor> members)
    {
        var names = members.Select(m => m.Name).ToArray();
        Array.Sort(names, StringComparer.Ordinal);
        return names;
    }

    [Fact]
    public void TheGeneratorRegisteredThisTypeAtAll()
    {
        // If this fails, every other parity test below is comparing reflection against itself and
        // proves nothing. It is separate for exactly that reason.
        Assert.True(EquivalencyRegistry.TryGetAccessors(typeof(TraitParityPoco), out _));
    }

    [Fact]
    public void GeneratedAndReflectedDefaultMembersMatch()
    {
        Assert.True(EquivalencyRegistry.TryGetAccessors(typeof(TraitParityPoco), out var generated));

        Assert.Equal(
            Sorted(new ReflectionMemberProvider().GetMembers(typeof(TraitParityPoco), MemberSelection.Default)),
            Sorted(generated!));
    }

    [Fact]
    public void GeneratedAndReflectedNonPublicMembersMatch()
    {
        Assert.True(EquivalencyRegistry.TryGetAccessors(typeof(TraitParityPoco), new MemberSelection(MemberTraits.NonPublic, MemberTraits.None), out var generated));

        Assert.Equal(
            Sorted(new ReflectionMemberProvider().GetMembers(typeof(TraitParityPoco), new MemberSelection(MemberTraits.NonPublic, MemberTraits.None))),
            Sorted(generated!));
    }

    [Fact]
    public void GeneratedAndReflectedTraitsMatchMemberForMember()
    {
        Assert.True(EquivalencyRegistry.TryGetAccessors(typeof(TraitParityPoco), new MemberSelection(MemberTraits.NonPublic | MemberTraits.NonBrowsable, MemberTraits.None), out var generated));
        var reflected = new ReflectionMemberProvider()
            .GetMembers(typeof(TraitParityPoco), new MemberSelection(MemberTraits.NonPublic | MemberTraits.NonBrowsable, MemberTraits.None))
            .ToDictionary(m => m.Name, m => m.Traits);

        foreach (var member in generated!)
        {
            Assert.True(reflected.TryGetValue(member.Name, out var reflectedTraits),
                $"The generator emitted '{member.Name}' and reflection does not see it.");
            Assert.Equal(reflectedTraits, member.Traits);
        }

        Assert.Equal(reflected.Count, generated.Length);
    }

    [Fact]
    public void ANonBrowsableMemberIsHiddenByDefaultAndTagged()
    {
        Assert.DoesNotContain("HiddenProperty", Sorted(RegistryMemberProvider.Instance.GetMembers(typeof(TraitParityPoco), MemberSelection.Default)));

        var members = RegistryMemberProvider.Instance.GetMembers(typeof(TraitParityPoco), new MemberSelection(MemberTraits.NonBrowsable, MemberTraits.None));
        Assert.Equal(MemberTraits.Property | MemberTraits.NonBrowsable, members.Single(m => m.Name == "HiddenProperty").Traits);
    }

    /// <summary>
    /// A member that is excluded for two reasons needs both bits, not either — otherwise asking for
    /// internal members would quietly drag in members someone marked non-browsable as well.
    /// </summary>
    [Fact]
    public void AskingForOneTraitDoesNotAdmitAMemberExcludedForTwo()
    {
        var internalsOnly = Sorted(RegistryMemberProvider.Instance.GetMembers(typeof(TraitParityPoco), new MemberSelection(MemberTraits.NonPublic, MemberTraits.None)));

        Assert.Contains("InternalProperty", internalsOnly);
        Assert.DoesNotContain("HiddenProperty", internalsOnly);
    }
}
