namespace MintPlayer.StringExtensions.Tests;

public class RandomStringTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    [Fact]
    public async Task RandomString_DefaultsToTwentyCharacters()
        => (await new Random().RandomString()).Should().HaveLength(20);

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(128)]
    public async Task RandomString_HonoursTheRequestedLength(int length)
        => (await new Random().RandomString(length)).Should().HaveLength(length);

    [Fact]
    public async Task RandomString_OnZeroLength_ReturnsEmpty()
        => (await new Random().RandomString(0)).Should().Be(string.Empty);

    [Fact]
    public async Task RandomString_UsesOnlyTheDocumentedAlphabet()
    {
        var result = await new Random().RandomString(500);
        result.ToCharArray().Should().OnlyContain(c => Alphabet.Contains(c));
    }

    [Fact]
    public async Task RandomString_WithASeededRandom_IsReproducible()
    {
        var first = await new Random(12345).RandomString(30);
        var second = await new Random(12345).RandomString(30);

        second.Should().Be(first);
    }

    [Fact]
    public async Task RandomString_WithDifferentSeeds_Differs()
    {
        var first = await new Random(1).RandomString(30);
        var second = await new Random(2).RandomString(30);

        second.Should().NotBe(first);
    }
}
