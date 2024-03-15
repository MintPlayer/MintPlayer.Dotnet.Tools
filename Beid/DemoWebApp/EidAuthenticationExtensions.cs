namespace DemoWebApp;

public static class EidAuthenticationExtensions
{
    //public static WebApplication UseEidAuthentication(this WebApplication app)
    //{
    //    return UseEidAuthentication(app, new EidAuthenticationOptions());
    //}

    public static IApplicationBuilder UseEidAuthentication(this WebApplication app)
    {
        return app.UseMiddleware<EidAuthenticationMiddleware>();
    }
}