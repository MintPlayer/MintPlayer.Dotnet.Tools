using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// A type that is never compared in this assembly — it is opted in explicitly, so only the
/// source generator can put it in the registry.
/// </summary>
[AssertEquivalency]
public class AttributedAccessorPoco
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Nickname = string.Empty;

    // Emitted since Phase 2 M5, tagged NonPublic: the generated file sits in the same assembly, so
    // it can reach an internal member. Whether the comparison uses it is the options' decision
    // (IncludingInternalMembers), not the generator's — which is what lets one option mean the same
    // thing for a scanned type and a reflected one.
    internal string Secret { get; set; } = string.Empty;

    // Still not eligible: a static member belongs to no instance.
    public static string Shared { get; set; } = string.Empty;
}

/// <summary>A type that carries no attribute: it can only be discovered through the call site below.</summary>
public class CallSiteAccessorPoco
{
    public string City { get; set; } = string.Empty;
    public int Number { get; set; }
}

public class GeneratorEquivalencyRegistrationTests
{
    [Fact]
    public void AssertEquivalencyAttribute_Registers_GeneratedAccessors()
    {
        Assert.True(EquivalencyRegistry.TryGetAccessors(typeof(AttributedAccessorPoco), out var members));

        var names = members!.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Age", "Name", "Nickname", "Secret" }, names);

        // The traits are what make the member filtering possible at all, so they are asserted here
        // rather than only through the options that consume them.
        Assert.Equal(MemberTraits.NonPublic, members!.Single(m => m.Name == "Secret").Traits);
        Assert.Equal(MemberTraits.None, members!.Single(m => m.Name == "Age").Traits);
        Assert.Equal(MemberTraits.Field, members!.Single(m => m.Name == "Nickname").Traits);

        var accessor = members!.Single(m => m.Name == "Name");
        Assert.True(accessor.IsProperty);
        Assert.Equal(typeof(string), accessor.Type);
        Assert.Equal("John", accessor.Getter(new AttributedAccessorPoco { Name = "John" }));

        var field = members!.Single(m => m.Name == "Nickname");
        Assert.False(field.IsProperty);
        Assert.Equal("Johnny", field.Getter(new AttributedAccessorPoco { Nickname = "Johnny" }));
    }

    [Fact]
    public void BeEquivalentTo_CallSite_Registers_ExpectationType()
    {
        var subject = new CallSiteAccessorPoco { City = "Ghent", Number = 9000 };
        var expectation = new CallSiteAccessorPoco { City = "Ghent", Number = 9000 };

        // The only mention of CallSiteAccessorPoco in an assertion: the generator must pick the
        // expectation's type up from this call site alone.
        ((object)subject).Should().BeEquivalentTo(expectation);

        Assert.True(EquivalencyRegistry.TryGetAccessors(typeof(CallSiteAccessorPoco), out var members));
        var names = members!.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "City", "Number" }, names);
    }
}
