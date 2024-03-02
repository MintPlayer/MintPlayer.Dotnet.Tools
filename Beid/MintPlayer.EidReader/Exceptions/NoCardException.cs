namespace MintPlayer.EidReader.Exceptions;

public class NoCardException : ReaderException
{
    public NoCardException(string msg) : base(msg) { }
}