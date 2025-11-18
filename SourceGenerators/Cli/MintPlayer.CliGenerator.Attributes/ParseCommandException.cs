using System.CommandLine.Parsing;

namespace MintPlayer.CliGenerator.Attributes;

public sealed class ParseCommandException : Exception
{
    public ParseCommandException(IEnumerable<string> tokens, IEnumerable<ParseError> errors)
    {
        Tokens = tokens;
        Errors = errors;
    }

    public IEnumerable<string> Tokens { get; }
    public IEnumerable<ParseError> Errors { get; }
}
