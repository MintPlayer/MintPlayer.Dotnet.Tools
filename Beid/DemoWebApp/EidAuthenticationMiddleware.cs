using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DemoWebApp;

public class EidAuthenticationMiddleware : AuthenticationMiddleware
{
    //private readonly RequestDelegate next;
    public EidAuthenticationMiddleware(RequestDelegate next, IAuthenticationSchemeProvider schemes)
        : base(next, schemes)
    {
        //this.next = next;
    }

    //public async Task InvokeAsync(HttpContext context)
    //{
    //    await next(context);
    //}
}
