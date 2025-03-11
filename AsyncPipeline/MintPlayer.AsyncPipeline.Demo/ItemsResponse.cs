namespace MintPlayer.AsyncPipeline.Demo;

public class ItemsResponse
{
    public Item[] Items { get; set; }
    public int PageStart { get; set; }
    public bool HasMore { get; set; }
    public int PageEnd => PageStart + Items.Length - 1;
}
