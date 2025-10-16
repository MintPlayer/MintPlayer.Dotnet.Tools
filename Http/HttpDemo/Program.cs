using MintPlayer.Http;

using var client = new HttpClient();

var created = await client.FromJsonAsync<WidgetDto>(new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/widgets")
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget("Minty", "green")));


record CreateWidget(string Name, string Color);
record WidgetDto(string Id, string Name, string Color);
