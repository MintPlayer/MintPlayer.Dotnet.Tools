using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// A dictionary's own key comparer must drive key lookups. Comparing with
/// EqualityComparer&lt;TKey&gt;.Default instead produced a false failure on ContainKey and — worse —
/// a silent false pass on NotContainKey for a key the dictionary really holds.
/// </summary>
public class DictionaryComparerTests
{
    private static Dictionary<string, string> CaseInsensitive() =>
        new(StringComparer.OrdinalIgnoreCase) { ["Newtonsoft.Json"] = "13.0.3" };

    [Fact]
    public void ContainKey_HonoursTheDictionaryComparer()
    {
        var versions = CaseInsensitive();
        versions.Should().ContainKey("newtonsoft.json").Which.Should().Be("13.0.3");
    }

    [Fact]
    public void Contain_KeyValue_HonoursTheDictionaryComparer()
    {
        var versions = CaseInsensitive();
        versions.Should().Contain("NEWTONSOFT.JSON", "13.0.3");
    }

    [Fact]
    public void ContainKeys_HonoursTheDictionaryComparer()
    {
        var versions = CaseInsensitive();
        versions.Should().ContainKeys("newtonsoft.json");
    }

    [Fact]
    public void NotContainKey_DoesNotSilentlyPassForAPresentKey()
    {
        var versions = CaseInsensitive();

        var ex = Record.Exception(() => versions.Should().NotContainKey("newtonsoft.json"));

        var failure = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", failure.Message);
        Assert.Contains("to contain key", failure.Message);
    }

    [Fact]
    public void NotContain_KeyValue_DoesNotSilentlyPassForAPresentPair()
    {
        var versions = CaseInsensitive();

        var ex = Record.Exception(() => versions.Should().NotContain("newtonsoft.json", "13.0.3"));

        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void PlainPairSequences_StillUseTheDefaultComparer()
    {
        // Not a dictionary: there is no comparer to honour, so casing matters again.
        var pairs = new List<KeyValuePair<string, string>> { new("Newtonsoft.Json", "13.0.3") };

        pairs.Should().NotContainKey("newtonsoft.json");
        pairs.Should().ContainKey("Newtonsoft.Json").Which.Should().Be("13.0.3");
    }

    [Fact]
    public void NullKeysAreLookedUpWithoutThrowing()
    {
        var pairs = new List<KeyValuePair<string?, int>> { new(null, 1) };

        pairs.Should().ContainKey(null).Which.Should().Be(1);
    }
}
