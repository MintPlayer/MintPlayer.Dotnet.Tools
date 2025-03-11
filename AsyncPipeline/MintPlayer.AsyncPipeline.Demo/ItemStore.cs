namespace MintPlayer.AsyncPipeline.Demo;

public class ItemStore
{
    private readonly Item[] items = Enumerable.Range(0, 500).Select(i => new Item { Id = i, Name = $"Item {i}" }).ToArray();

    public async Task<ItemsResponse> GetItems(int offset, int count)
    {
        await Task.Delay(2000);
        return new ItemsResponse
        {
            Items = items.Skip(offset).Take(count).ToArray(),
            PageStart = offset,
            HasMore = (offset + count < items.Length),
        };
    }
}