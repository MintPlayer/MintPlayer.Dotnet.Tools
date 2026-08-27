using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SeasonChecker.Abstractions;
using MintPlayer.SeasonChecker.Abstractions.Extensions;

namespace MintPlayer.SeasonChecker.Tests;

/// <summary>
/// The implementation is internal, so it is resolved through DI exactly as a consumer
/// would. It contains no DateTime.Now, so every case here is deterministic.
/// </summary>
public class SeasonCheckerTests
{
    private sealed class Season : ISeason
    {
        public string Name { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }

    private static ISeasonChecker CreateChecker()
        => new ServiceCollection()
            .AddSeasonChecker()
            .BuildServiceProvider()
            .GetRequiredService<ISeasonChecker>();

    #region DI registration

    [Fact]
    public void AddSeasonChecker_RegistersTheCheckerAsScoped()
    {
        var services = new ServiceCollection().AddSeasonChecker();

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(ISeasonChecker)).Which;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSeasonChecker_ReturnsTheSameCollection_ForChaining()
    {
        var services = new ServiceCollection();
        services.AddSeasonChecker().Should().BeSameAs(services);
    }

    [Fact]
    public void AddSeasonChecker_ResolvesFromAScope()
    {
        using var provider = new ServiceCollection().AddSeasonChecker().BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISeasonChecker>().Should().NotBeNull();
    }

    #endregion

    #region Built-in northern hemisphere seasons

    [Theory]
    [InlineData(3, 21, "Spring")]
    [InlineData(5, 1, "Spring")]
    [InlineData(6, 20, "Spring")]
    [InlineData(6, 21, "Summer")]
    [InlineData(8, 15, "Summer")]
    [InlineData(9, 20, "Summer")]
    [InlineData(9, 21, "Automn")]
    [InlineData(11, 1, "Automn")]
    [InlineData(12, 20, "Automn")]
    [InlineData(12, 21, "Winter")]
    [InlineData(1, 15, "Winter")]
    [InlineData(3, 20, "Winter")]
    public async Task FindSeasonAsync_Northern_MapsDateToSeason(int month, int day, string expected)
    {
        var season = await CreateChecker().FindSeasonAsync<Season>(
            EHemisphere.Northern, new DateTime(2026, month, day));

        season.Should().NotBeNull();
        season!.Name.Should().Be(expected);
    }

    #endregion

    #region Built-in southern hemisphere seasons

    [Theory]
    [InlineData(3, 21, "Automn")]
    [InlineData(6, 21, "Winter")]
    [InlineData(9, 21, "Spring")]
    [InlineData(12, 21, "Summer")]
    [InlineData(1, 15, "Summer")]
    public async Task FindSeasonAsync_Southern_MapsDateToSeason(int month, int day, string expected)
    {
        var season = await CreateChecker().FindSeasonAsync<Season>(
            EHemisphere.Southern, new DateTime(2026, month, day));

        season.Should().NotBeNull();
        season!.Name.Should().Be(expected);
    }

    [Fact]
    public async Task TheTwoHemispheres_AreOppositeOnTheSameDate()
    {
        var checker = CreateChecker();
        var date = new DateTime(2026, 7, 15);

        var north = await checker.FindSeasonAsync<Season>(EHemisphere.Northern, date);
        var south = await checker.FindSeasonAsync<Season>(EHemisphere.Southern, date);

        north!.Name.Should().Be("Summer");
        south!.Name.Should().Be("Winter");
    }

    #endregion

    #region Boundaries and edge cases

    [Fact]
    public async Task FindSeasonAsync_CoversEveryDayOfTheYear()
    {
        var checker = CreateChecker();

        // 2024 is a leap year, so this walks 29 February too.
        for (var date = new DateTime(2024, 1, 1); date.Year == 2024; date = date.AddDays(1))
        {
            var season = await checker.FindSeasonAsync<Season>(EHemisphere.Northern, date);
            season.Should().NotBeNull($"every day should map to a season, but {date:MM-dd} did not");
        }
    }

    [Fact]
    public async Task FindSeasonAsync_HandlesTheLeapDay()
    {
        // The implementation remaps every season onto the year 2000, itself a leap year,
        // so 29 February must not throw.
        var season = await CreateChecker().FindSeasonAsync<Season>(
            EHemisphere.Northern, new DateTime(2024, 2, 29));

        season!.Name.Should().Be("Winter");
    }

    [Fact]
    public async Task FindSeasonAsync_IgnoresTheYearOfTheQueriedDate()
    {
        var checker = CreateChecker();

        var first = await checker.FindSeasonAsync<Season>(EHemisphere.Northern, new DateTime(1900, 7, 4));
        var second = await checker.FindSeasonAsync<Season>(EHemisphere.Northern, new DateTime(2100, 7, 4));

        second!.Name.Should().Be(first!.Name);
    }

    [Fact]
    public async Task FindSeasonAsync_WithAnUnknownHemisphere_Throws()
    {
        var act = () => CreateChecker().FindSeasonAsync<Season>((EHemisphere)99, new DateTime(2026, 1, 1));
        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("hemisphere");
    }

    #endregion

    #region Custom season sets

    [Fact]
    public async Task FindSeasonAsync_AcceptsACustomSeasonSet()
    {
        var seasons = new[]
        {
            new Season { Name = "Dry",  Start = new DateTime(2000, 5, 1),  End = new DateTime(2000, 10, 31) },
            new Season { Name = "Rainy", Start = new DateTime(2000, 11, 1), End = new DateTime(2001, 4, 30) },
        };

        var checker = CreateChecker();

        (await checker.FindSeasonAsync(seasons, new DateTime(2026, 7, 1)))!.Name.Should().Be("Dry");
        (await checker.FindSeasonAsync(seasons, new DateTime(2026, 12, 1)))!.Name.Should().Be("Rainy");
        // The new-year-crossing season is split, so January resolves to the same season.
        (await checker.FindSeasonAsync(seasons, new DateTime(2026, 1, 15)))!.Name.Should().Be("Rainy");
    }

    [Fact]
    public async Task FindSeasonAsync_ReturnsTheOriginalInstance_NotACopy()
    {
        var summer = new Season { Name = "Only", Start = new DateTime(2000, 1, 1), End = new DateTime(2000, 12, 31) };

        var found = await CreateChecker().FindSeasonAsync([summer], new DateTime(2026, 6, 1));

        found.Should().BeSameAs(summer);
    }

    [Fact]
    public async Task FindSeasonAsync_WhenNoSeasonCoversTheDate_ReturnsNull()
    {
        var seasons = new[]
        {
            new Season { Name = "Narrow", Start = new DateTime(2000, 6, 1), End = new DateTime(2000, 6, 30) },
        };

        (await CreateChecker().FindSeasonAsync(seasons, new DateTime(2026, 1, 1))).Should().BeNull();
    }

    [Fact]
    public async Task FindSeasonAsync_OnAnEmptySeasonSet_ReturnsNull()
        => (await CreateChecker().FindSeasonAsync(Array.Empty<Season>(), new DateTime(2026, 1, 1)))
            .Should().BeNull();

    [Fact]
    public async Task FindSeasonAsync_WithOverlappingSeasons_ReturnsTheFirstMatch()
    {
        var seasons = new[]
        {
            new Season { Name = "First",  Start = new DateTime(2000, 1, 1), End = new DateTime(2000, 12, 31) },
            new Season { Name = "Second", Start = new DateTime(2000, 6, 1), End = new DateTime(2000, 6, 30) },
        };

        (await CreateChecker().FindSeasonAsync(seasons, new DateTime(2026, 6, 15)))!.Name.Should().Be("First");
    }

    #endregion
}
