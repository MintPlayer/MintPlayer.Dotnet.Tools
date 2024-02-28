namespace MintPlayer.EidReader.Events.EventArgs;

public class ReaderAttachedEventArgs : System.EventArgs
{
    public required string ReaderName { get; init; }
}
