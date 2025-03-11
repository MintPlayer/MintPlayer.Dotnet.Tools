
using MintPlayer.AsyncPipeline.Demo.Extensions;
using MintPlayer.AsyncPipeline.Demo;
using MintPlayer.AsyncPipeline;

var store = new ItemStore();


var pipelineA = Pipeline<ItemsResponse>.Create(async (pageNumber, consumerId, output) =>
{
    ConsoleEx.WriteLine($"[A] Fetching page {pageNumber}");
    var items = await store.GetItems(pageNumber * 25, 25);
    await output.Writer.WriteAsync(items);
    ConsoleEx.WriteLine($"""
        [A] Fetched page {pageNumber}
            {string.Join(',', items.Items.Select(x => x.Name))}
        """);

    return items.HasMore;
});

var pipelineB = pipelineA.Concat<ItemsResponse>(async (pageNumber, consumerId, input, output) =>
{
    await foreach (var page in input.Reader.ReadAllAsync())
    {
        ConsoleEx.WriteLine($"[B] Reversing {page.PageStart} to {page.PageEnd} (Consumer {consumerId})");
        await Task.Delay(5000); // Simulate processing
        var reversed = new ItemsResponse
        {
            PageStart = page.PageStart,
            HasMore = page.HasMore,
            Items = page.Items.Select(x => new Item
            {
                Id = x.Id,
                Name = new string(x.Name.Reverse().ToArray()),
            }).ToArray(),
        };
        await output.Writer.WriteAsync(reversed);
        ConsoleEx.WriteLine($"""
            [B] Reversed {reversed.PageStart} to {reversed.PageEnd} (Consumer {consumerId})
                {string.Join(',', reversed.Items.Select(x => x.Name))}
            """);

    }
    return false;
}, 3);

var pipelineC = pipelineB.Concat<ItemsResponse>(async (pageNumber, consumerId, input, output) =>
{
    await foreach (var page in input.Reader.ReadAllAsync())
    {
        ConsoleEx.WriteLine($"[C] Capsing {page.PageStart} to {page.PageEnd} (Consumer {consumerId})");
        await Task.Delay(6000); // Simulate processing
        var capsed = new ItemsResponse
        {
            PageStart = page.PageStart,
            HasMore = page.HasMore,
            Items = page.Items.Select(x => new Item
            {
                Id = x.Id,
                Name = new string(x.Name.ToUpper()),
            }).ToArray(),
        };
        ConsoleEx.WriteLine($"""
            [C] Capsed {capsed.PageStart} to {capsed.PageEnd} (Consumer {consumerId})
                {string.Join(',', capsed.Items.Select(x => x.Name))}
            """);

    }
    return false;
}, 3);


// TODO: Make awaiter await inner-pipeline, so that we only have one call instead of 3 here
await pipelineA;
await pipelineB;
await pipelineC;