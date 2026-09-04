using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Covers the equivalency overloads reachable from a typed collection or dictionary subject:
/// the collection <c>NotBeEquivalentTo</c> mirror, and both forms for dictionaries. Before these
/// existed the only route was a cast to <c>object</c>, which loses the generated member accessors
/// and makes the options lambda untypeable.
/// </summary>
public class CollectionAndDictionaryEquivalencyTests
{
    private sealed class Lane
    {
        public string Name { get; set; } = "";
        public int Width { get; set; }
    }

    // ---------------------------------------------------------------------------------------
    // Collections: the NotBeEquivalentTo mirror
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void NotBeEquivalentTo_Passes_ForCollectionsThatDiffer()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }];
        Lane[] expectation = [new() { Name = "right", Width = 3 }];

        subject.Should().NotBeEquivalentTo(expectation);
    }

    [Fact]
    public void NotBeEquivalentTo_Fails_ForEquivalentCollections()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }];
        Lane[] expectation = [new() { Name = "left", Width = 3 }];

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no differences were found", ex!.Message);
    }

    [Fact]
    public void NotBeEquivalentTo_Passes_ForCollectionsOfDifferentLength()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }, new() { Name = "right", Width = 4 }];
        Lane[] expectation = [new() { Name = "left", Width = 3 }];

        subject.Should().NotBeEquivalentTo(expectation);
    }

    /// <summary>Unordered matching is the default, so a reordered collection is still equivalent.</summary>
    [Fact]
    public void NotBeEquivalentTo_Fails_ForReorderedCollection_ByDefault()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }, new() { Name = "right", Width = 4 }];
        Lane[] expectation = [new() { Name = "right", Width = 4 }, new() { Name = "left", Width = 3 }];

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
    }

    /// <summary>The typed options lambda is the whole point of having the overload — prove it binds.</summary>
    [Fact]
    public void NotBeEquivalentTo_HonoursTheTypedOptionsLambda()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }];
        Lane[] expectation = [new() { Name = "left", Width = 99 }];

        // Width is the only difference; excluding it makes the collections equivalent, so the
        // negative assertion must fail.
        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(expectation,
            o => o.ExcludingNested<Lane>(x => x.Width)));

        Assert.IsType<AssertionFailedException>(ex);

        // Without the exclusion the same pair differs, so it must pass.
        subject.Should().NotBeEquivalentTo(expectation);
    }

    /// <summary>
    /// Pins a sharp edge of the collection overloads: <c>Excluding</c> records a path relative to
    /// the comparison root, and on a collection subject the root is the collection — so an element
    /// member sits at <c>"[?].Width"</c> and a root-relative <c>"Width"</c> never matches. The
    /// exclusion is silently ignored. Use <c>ExcludingNested&lt;T&gt;</c> (above) or a wildcard
    /// path for element members. This only ever makes a comparison stricter than intended, so it
    /// surfaces as a failing assertion rather than a silent pass.
    /// </summary>
    [Fact]
    public void Excluding_IsRootRelative_AndSoDoesNotReachCollectionElementMembers()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }];
        Lane[] expectation = [new() { Name = "left", Width = 99 }];

        // The exclusion does not apply, so the collections still differ and the positive form fails.
        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.Excluding(x => x.Width)));
        Assert.IsType<AssertionFailedException>(ex);

        // A wildcard path does reach the element member.
        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingPath("*.Width"));
    }

    [Fact]
    public void NotBeEquivalentTo_HonoursStrictOrdering()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }, new() { Name = "right", Width = 4 }];
        Lane[] expectation = [new() { Name = "right", Width = 4 }, new() { Name = "left", Width = 3 }];

        subject.Should().NotBeEquivalentTo(expectation, o => o.WithStrictOrdering());
    }

    [Fact]
    public void NotBeEquivalentTo_WorksForListSubjects()
    {
        var subject = new List<Lane> { new() { Name = "left", Width = 3 } };
        var expectation = new List<Lane> { new() { Name = "right", Width = 3 } };

        subject.Should().NotBeEquivalentTo(expectation);
    }

    /// <summary>A collection compared against anonymous objects holding only the interesting members.</summary>
    [Fact]
    public void BeEquivalentTo_Passes_ForCollectionAgainstAnonymousSubset()
    {
        Lane[] subject = [new() { Name = "left", Width = 3 }];

        subject.Should().BeEquivalentTo(new[] { new { Name = "left" } });
    }

    // ---------------------------------------------------------------------------------------
    // Dictionaries
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BeEquivalentTo_Passes_ForEquivalentDictionaries()
    {
        var subject = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };
        var expectation = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void BeEquivalentTo_Fails_ForDifferingValue_AndNamesTheKey()
    {
        var subject = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };
        var expectation = new Dictionary<string, Lane> { ["a"] = new() { Name = "right", Width = 3 } };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("[a]", ex!.Message);
        Assert.Contains("\"right\"", ex.Message);
        Assert.Contains("\"left\"", ex.Message);
    }

    [Fact]
    public void BeEquivalentTo_Fails_ForMissingKey()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1 };
        var expectation = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("\"b\"", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Fails_ForUnexpectedKey()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var expectation = new Dictionary<string, int> { ["a"] = 1 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("unexpected key", ex!.Message);
    }

    [Fact]
    public void NotBeEquivalentTo_Passes_ForDictionariesThatDiffer()
    {
        var subject = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };
        var expectation = new Dictionary<string, Lane> { ["a"] = new() { Name = "right", Width = 3 } };

        subject.Should().NotBeEquivalentTo(expectation);
    }

    [Fact]
    public void NotBeEquivalentTo_Fails_ForEquivalentDictionaries()
    {
        var subject = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };
        var expectation = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no differences were found", ex!.Message);
    }

    /// <summary>Dictionary values compared against anonymous objects holding a subset of members.</summary>
    [Fact]
    public void BeEquivalentTo_Passes_ForDictionaryValuesAgainstAnonymousSubset()
    {
        var subject = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };

        subject.Should().BeEquivalentTo(new Dictionary<string, object> { ["a"] = new { Name = "left" } });
    }

    [Fact]
    public void BeEquivalentTo_HonoursTheTypedOptionsLambda_ForDictionaries()
    {
        var subject = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 3 } };
        var expectation = new Dictionary<string, Lane> { ["a"] = new() { Name = "left", Width = 99 } };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingNested<Lane>(x => x.Width));

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void BeEquivalentTo_Passes_ForTwoEmptyDictionaries()
    {
        new Dictionary<string, Lane>().Should().BeEquivalentTo(new Dictionary<string, Lane>());
    }
}
