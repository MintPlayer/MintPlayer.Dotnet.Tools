using Microsoft.AspNetCore.Mvc;
using System.Runtime.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddControllers(options => options.RespectBrowserAcceptHeader = true)
    .AddXmlDataContractSerializerFormatters();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthorization();

// Map endpoints
app.MapControllers();

app.Run();

namespace WidgetApi.Contracts
{

    [DataContract(Name = "CreateWidget", Namespace = "http://schemas.example.com/widgets")]
    public class CreateWidget
    {
        [DataMember(Order = 1)] public string Name { get; set; } = default!;
        [DataMember(Order = 2)] public string Color { get; set; } = default!;
    }

    [DataContract(Name = "WidgetDto", Namespace = "http://schemas.example.com/widgets")]
    public class WidgetDto
    {
        [DataMember(Order = 1)] public string Id { get; set; } = default!;
        [DataMember(Order = 2)] public string Name { get; set; } = default!;
        [DataMember(Order = 3)] public string Color { get; set; } = default!;
    }

    [DataContract(Name = "WidgetCreatedResponse", Namespace = "http://schemas.example.com/widgets")]
    public class WidgetCreatedResponse
    {
        [DataMember(Order = 1)] public WidgetDto Widget { get; set; } = default!;
        [DataMember(Order = 2)] public Dictionary<string, string> Headers { get; set; } = default!;
    }
}

[ApiController]
[Route("[controller]")]
public class WidgetController : ControllerBase
{
    [HttpPost]
    public ActionResult<WidgetApi.Contracts.WidgetCreatedResponse> Create([FromBody] WidgetApi.Contracts.CreateWidget input) // The error is explicitly about this "input" field
    {
        return Ok(new WidgetApi.Contracts.WidgetCreatedResponse
        {
            Widget = new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = input.Name,
                Color = input.Color,
            },
            Headers = Request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value.Select(x => x))),
        });
    }
}