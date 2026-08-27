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

    // Not eligible: non-public, static and write-only members are never accessors.
    internal string Secret { get; set; } = string.Empty;
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
        Assert.Equal(new[] { "Age", "Name", "Nickname" }, names);

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
