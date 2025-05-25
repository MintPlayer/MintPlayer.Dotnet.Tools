namespace MintPlayer.CliParser.Abstractions;

public interface ICliParser
{
    IEnumerable<string> ParseArguments(string[] args);
}
