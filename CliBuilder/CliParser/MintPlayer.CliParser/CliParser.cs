using MintPlayer.CliParser.Abstractions;
using MintPlayer.CliParser.Extensions;

namespace MintPlayer.CliParser;

internal class CliParser : ICliParser
{
    public IEnumerable<string> ParseArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        //var parsedArgs = new List<string>();
        //foreach (var arg in args)
        //{
        //    if (arg.StartsWith("--"))
        //    {
        //        // Long option
        //        parsedArgs.Add(arg.Substring(2));
        //    }
        //    else if (arg.StartsWith("-"))
        //    {
        //        // Short option
        //        parsedArgs.Add(arg.Substring(1));
        //    }
        //    else
        //    {
        //        // Positional argument
        //        parsedArgs.Add(arg);
        //    }
        //}
        return args.SelectMany(a => a.SplitEqualsSign('='));
    }
}
