namespace MintPlayer.EidReader.Events.EventArgs;

public class CardRemovedEventArgs : System.EventArgs
{
    public required string ReaderName { get; init; }
}
