using MintPlayer.SeasonChecker.Abstractions;

namespace MintPlayer.SeasonChecker;

internal class SeasonChecker : ISeasonChecker
{
    public Task<TSeason?> FindSeasonAsync<TSeason>(EHemisphere hemisphere, DateTime date) where TSeason : class, ISeason, new()
    {
        IEnumerable<TSeason> seasons;
        switch (hemisphere)
        {
            case EHemisphere.Northern:
                seasons = new List<TSeason>
                    {
                        new TSeason
                        {
                            Name = "Spring",
                            Start = new DateTime(2000, 3, 21),
                            End = new DateTime(2000, 6, 20)
                        },
                        new TSeason
                        {
                            Name = "Summer",
                            Start = new DateTime(2000, 6, 21),
                            End = new DateTime(2000, 9, 20)
                        },
                        new TSeason
                        {
                            Name = "Automn",
                            Start = new DateTime(2000, 9, 21),
                            End = new DateTime(2000, 12,20)
                        },
                        new TSeason
                        {
                            Name = "Winter",
                            Start = new DateTime(2000, 12, 21),
                            End = new DateTime(2001, 3, 20)
                        }
                    };
                break;
            case EHemisphere.Southern:
                seasons = new List<TSeason>
                    {
                        new TSeason
                        {
                            Name = "Automn",
                            Start = new DateTime(2000, 3, 21),
                            End = new DateTime(2000, 6, 20)
                        },
                        new TSeason
                        {
                            Name = "Winter",
                            Start = new DateTime(2000, 6, 21),
                            End = new DateTime(2000, 9, 20)
                        },
                        new TSeason
                        {
                            Name = "Spring",
                            Start = new DateTime(2000, 9, 21),
                            End = new DateTime(2000, 12,20)
                        },
                        new TSeason
                        {
                            Name = "Summer",
                            Start = new DateTime(2000, 12, 21),
                            End = new DateTime(2001, 3, 20)
                        }
                    };
                break;
            default:
                throw new ArgumentException("Values allowed for parameter hemisphere: Northern, Southern", nameof(hemisphere));
        }

        return FindSeasonAsync(seasons, date);
    }

    public Task<TSeason?> FindSeasonAsync<TSeason>(IEnumerable<TSeason> seasons, DateTime date) where TSeason : class, ISeason
    {
        var result = seasons
            .Select(s =>
            {
                // Find season that crosses newyear
                if (s.Start.Year == s.End.Year)
                {
                    return new[] {
                            new {
                                OriginalSeason = s,
                                // Remap the season to the year 2000
                                ProcessableSeason = new InternalSeason {
                                    Name = s.Name,
                                    Start = new DateTime(2000, s.Start.Month, s.Start.Day),
                                    End = new DateTime(2000, s.End.Month, s.End.Day)
                                }
                            }
                    };
                }
                else
                {
                    // If the season crosses the newyear, split the season
                    return new[] {
                            new {
                                OriginalSeason = s,
                                // Remap the season to the year 2000
                                ProcessableSeason = new InternalSeason {
                                    Name = s.Name,
                                    Start = new DateTime(2000, s.Start.Month, s.Start.Day),
                                    End = new DateTime(2000, 12, 31)
                                }
                            },
                            new {
                                OriginalSeason = s,
                                // Remap the season to the year 2000
                                ProcessableSeason = new InternalSeason {
                                    Name = s.Name,
                                    Start = new DateTime(2000, 1, 1),
                                    End = new DateTime(2000, s.End.Month, s.End.Day)
                                }
                            }
                    };
                }
            })
            .SelectMany(s => s)
            .FirstOrDefault(s =>
                // Now we can easily compare the dates.
                DateTime.Compare(s.ProcessableSeason.Start, new DateTime(2000, date.Month, date.Day)) <= 0 &&
                DateTime.Compare(new DateTime(2000, date.Month, date.Day), s.ProcessableSeason.End) <= 0
            )?.OriginalSeason;

        return Task.FromResult(result);
    }
}