using MintPlayer.Http;

const string requestUrl = "https://api.example.com/widgets";
using var client = new HttpClient();

var created = await client.SendAsync<WidgetDto>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget("Minty", "green"))
    .AcceptJson());

var (result1, status1) = await client.SendAsync<WidgetDto>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget("Minty", "green"))
    .AcceptJson());

var (result2, status2, headers2) = await client.SendAsync<WidgetDto>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget("Minty", "green"))
    .AcceptJson());

var (result3, status3, headers3, redirect3) = await client.SendAsync<WidgetDto>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget("Minty", "green"))
    .AcceptJson());


record CreateWidget(string Name, string Color);
record WidgetDto(string Id, string Name, string Color);
