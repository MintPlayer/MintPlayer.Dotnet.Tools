namespace MintPlayer.SeasonChecker.Abstractions;

public interface ISeasonChecker
{
    Task<TSeason?> FindSeasonAsync<TSeason>(EHemisphere hemisphere, DateTime date) where TSeason : class, ISeason, new();
    Task<TSeason?> FindSeasonAsync<TSeason>(IEnumerable<TSeason> seasons, DateTime date) where TSeason : class, ISeason;
}