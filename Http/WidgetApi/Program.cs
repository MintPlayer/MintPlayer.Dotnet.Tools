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
    [DataContract(Name = nameof(CreateWidget), Namespace = "http://schemas.example.com/widgets")]
    public class CreateWidget
    {
        [DataMember] public string Name { get; set; } = default!;
        [DataMember] public string Color { get; set; } = default!;
    }

    [DataContract(Name = nameof(WidgetDto), Namespace = "http://schemas.example.com/widgets")]
    public class WidgetDto
    {
        [DataMember] public string Id { get; set; } = default!;
        [DataMember] public string Name { get; set; } = default!;
        [DataMember] public string Color { get; set; } = default!;
    }

    [DataContract(Name = nameof(WidgetCreatedResponse), Namespace = "http://schemas.example.com/widgets")]
    public class WidgetCreatedResponse
    {
        [DataMember] public WidgetDto Widget { get; set; } = default!;
        [DataMember] public Dictionary<string, string> Headers { get; set; } = default!;
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