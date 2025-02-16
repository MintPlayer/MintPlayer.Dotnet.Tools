using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DemoWebApp.Controllers;

[Controller]
[Route("[controller]")]
public class TodoController : Controller
{
    [HttpGet("challenge")]
    public ActionResult GetPersonInfoChallenge()
    {
        return Challenge(CertificateAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = CertificateAuthenticationDefaults.AuthenticationScheme)]
    public async Task<ActionResult<PersonInfo>> GetPersonInfo()
    {
        //var cert = c.Connection.ClientCertificate;
        var cert = await HttpContext.Connection.GetClientCertificateAsync();
        var result = new PersonInfo(cert!);
        return Ok(result);
    }

    [HttpGet("test")]
    public async Task<ActionResult<Todo[]>> GetTest()
    {
        return Ok(new[]
        {
            new Todo(1, "Do this thing", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
            new Todo(2, "Do that thing", DateOnly.FromDateTime(DateTime.Now.AddDays(2)), true),
            new Todo(3, "Do the other thing", DateOnly.FromDateTime(DateTime.Now.AddDays(3))),
        });
    }
}
