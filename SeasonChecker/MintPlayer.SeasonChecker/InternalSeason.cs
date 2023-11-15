using MintPlayer.SeasonChecker.Abstractions;

namespace MintPlayer.SeasonChecker;

internal class InternalSeason : ISeason
{
    public string Name { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
