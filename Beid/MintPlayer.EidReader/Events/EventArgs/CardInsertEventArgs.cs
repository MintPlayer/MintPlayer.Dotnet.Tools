namespace MintPlayer.EidReader.Events.EventArgs;

public class CardInsertEventArgs : System.EventArgs
{
    public required Card Card { get; init; }
    public required string ReaderName { get; init; }
}
