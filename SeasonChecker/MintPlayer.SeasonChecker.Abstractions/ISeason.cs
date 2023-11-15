namespace MintPlayer.SeasonChecker.Abstractions;

public interface ISeason
{
    string Name { get; set; }
    DateTime Start { get; set; }
    DateTime End { get; set; }
}
