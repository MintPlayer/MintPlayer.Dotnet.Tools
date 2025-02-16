using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Asn1.X509;
using System.Net;
using System.Text.Json.Serialization;

namespace DemoWebApp;

public class Program
{
    public static void Main(string[] args)
    {
        ServicePointManager.ServerCertificateValidationCallback = (_, __, ___, ____) => true;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        var builder = WebApplication.CreateSlimBuilder(args);
        builder.Services.AddControllersWithViews().AddNewtonsoftJson();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ConfigureHttpsDefaults(http =>
            {
                // Fixes ERR_SSL_CLIENT_AUTH_SIGNATURE_FAILED
                http.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                http.ClientCertificateValidation = (_, __, ___) =>
                {
                    return true;
                };
                http.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
            });
        });
        builder.WebHost.UseKestrelHttpsConfiguration();
        var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseHttpsRedirection();
        app.UseDeveloperExceptionPage();
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });

        var todosApi = app.MapGroup("/todos");
        todosApi.MapGet("/", async (c) =>
        {
            var cert = c.Connection.ClientCertificate;
            var result = new PersonInfo(cert!);
            await c.Response.WriteAsJsonAsync(result);
        });

        app.Run();
    }
}

public class PersonInfo
{
    public PersonInfo(System.Security.Cryptography.X509Certificates.X509Certificate certificate)
    {
        var bcCert = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(certificate);
        FirstNames = bcCert.SubjectDN.GetValueList(X509Name.GivenName)[0]?.ToString() ?? string.Empty;
        LastName = bcCert.SubjectDN.GetValueList(X509Name.Surname)[0]?.ToString() ?? string.Empty;
        NationalNumber = bcCert.SubjectDN.GetValueList(X509Name.SerialNumber)[0]?.ToString() ?? string.Empty;
        Country = bcCert.SubjectDN.GetValueList(X509Name.C)[0]?.ToString() ?? string.Empty;
        //PostalAddress = bcCert.SubjectDN.GetValueList(X509Name.PostalAddress)[0]?.ToString() ?? string.Empty;
        //PostalCode = bcCert.SubjectDN.GetValueList(X509Name.PostalCode)[0]?.ToString() ?? string.Empty;
        //Street = bcCert.SubjectDN.GetValueList(X509Name.Street)[0]?.ToString() ?? string.Empty;
        //TelephoneNumber = bcCert.SubjectDN.GetValueList(X509Name.TelephoneNumber)[0]?.ToString() ?? string.Empty;
        NotBefore = bcCert.NotBefore;
        NotAfter = bcCert.NotAfter;
    }

    public DateTime NotBefore { get; private set; }
    public DateTime NotAfter { get; private set; }
    public string FirstNames { get; private set; }
    public string LastName { get; private set; }
    public string NationalNumber { get; private set; }
    public string Country { get; private set; }
    //public string PostalAddress { get; private set; }
    //public string PostalCode { get; private set; }
    //public string Street { get; private set; }
    //public string TelephoneNumber { get; private set; }
}

public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(PersonInfo))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public class User : IdentityUser<Guid> { }
public class Role : IdentityRole<Guid> { }

public class EidContext : IdentityDbContext<User, Role, Guid>
{
    public EidContext()
    {

    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseInMemoryDatabase("Eid");
    }
}
