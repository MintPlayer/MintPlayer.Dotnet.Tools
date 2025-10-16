using MintPlayer.Http;
using System.Diagnostics;
using WidgetApi.Contracts;

const string requestUrl = "https://localhost:7189/Widget";
using var client = new HttpClient();

var created = await client.SendAsync<WidgetCreatedResponse>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget { Name = "Minty", Color = "green" })
    .AcceptJson());

var (result1, status1) = await client.SendAsync<WidgetCreatedResponse>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithJsonContent(new CreateWidget { Name = "Minty", Color = "green" })
    .AcceptXml());

var (result2, status2, headers2) = await client.SendAsync<WidgetCreatedResponse>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithXmlContent(new CreateWidget { Name = "Minty", Color = "green" })
    .AcceptJson());

var (result3, status3, headers3, redirect3) = await client.SendAsync<WidgetCreatedResponse>(new HttpRequestMessage(HttpMethod.Post, requestUrl)
    .WithAuthorizationBearer("your_jwt_here")
    .WithHeader("X-TraceId", Guid.NewGuid().ToString())
    .WithXmlContent(new CreateWidget { Name = "Minty", Color = "green" })
    .AcceptXml());

Debugger.Break();
