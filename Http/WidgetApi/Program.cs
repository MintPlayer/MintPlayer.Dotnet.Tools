using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddControllers(options => options.RespectBrowserAcceptHeader = true)
    .AddXmlDataContractSerializerFormatters()
    .AddMvcOptions(options =>
    {
        if (options.OutputFormatters.OfType<Microsoft.AspNetCore.Mvc.Formatters.XmlDataContractSerializerOutputFormatter>().FirstOrDefault() is { } xmlFormatter)
        {
            xmlFormatter.WriterSettings.Indent = true;
            xmlFormatter.WriterSettings.OmitXmlDeclaration = false;
        }
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthorization();

// Map endpoints
app.MapControllers();

app.Run();


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
            RequestHeaders = Request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value.Select(x => x))),
        });
    }
}