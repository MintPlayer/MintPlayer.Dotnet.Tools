namespace MintPlayer.EidReader.Events.EventArgs;

public class ReaderDetachedEventArgs : System.EventArgs
{
    public required string ReaderName { get; init; }
}
