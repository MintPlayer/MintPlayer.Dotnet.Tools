namespace MintPlayer.EidReader.Core.Extensions;

internal static class StringExtensions
{
    internal static int ToMonth(this string value)
    {
        switch (value)
        {
            case "JAN":
                return 1;
            case "FEB":
            case "FEV":
                return 2;
            case "MÄR":
            case "MARS":
            case "MAAR":
                return 3;
            case "APR":
            case "AVR":
                return 4;
            case "MAI":
            case "MEI":
                return 5;
            case "JUIN":
            case "JUN":
                return 6;
            case "JUIL":
            case "JUL":
                return 7;
            case "AOUT":
            case "AUG":
                return 8;
            case "SEPT":
            case "SEP":
                return 9;
            case "OCT":
            case "OKT":
                return 10;
            case "NOV":
                return 11;
            case "DEC":
            case "DEZ":
                return 12;
            default:
                throw new InvalidOperationException("Unknown Birth Month: " + value);
        }
    }
}
