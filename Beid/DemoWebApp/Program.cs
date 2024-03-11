using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace DemoWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.Configure<EidAuthenticationOptions>(options =>
            {
                options.SignInAsAuthenticationType = "Cookies";
            });

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)//("Eid")
                .AddCertificate(o =>
                {
                    o.AllowedCertificateTypes = CertificateTypes.All;
                    //o.ChainTrustValidationMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.System;
                    o.ChainTrustValidationMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                    o.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                    o.CustomTrustStore = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection
                    {

                    };

                    o.Events = new CertificateAuthenticationEvents
                    {
                        OnChallenge = async (context) =>
                        {

                        },
                        OnAuthenticationFailed = async (context) =>
                        {

                        },
                        OnCertificateValidated = async (context) =>
                        {
                            var thb = context.ClientCertificate?.Thumbprint;
                            context.Principal = new ClaimsPrincipal(new ClaimsIdentity([], context.Scheme.Name));
                            context.Success();
                        }
                    };
                })
                .AddScheme<EidAuthenticationOptions, EidAuthenticationHandler>("Eid", o =>
                {
                    var x = o.Events;
                });

            builder.Services.AddAuthorization(o =>
            {
                o.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes("Eid")
                    .RequireAuthenticatedUser()
                    .Build();
            });

            builder.Services.AddControllersWithViews();

            builder.Services.AddDbContext<EidContext>();

            builder.Services.AddIdentity<User, Role>()
                .AddEntityFrameworkStores<EidContext>()
                .AddDefaultTokenProviders();

            builder.WebHost.ConfigureKestrel(k =>
            {
                k.ConfigureHttpsDefaults(http =>
                {
                    http.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                    http.AllowAnyClientCertificate();
                    http.CheckCertificateRevocation = false;
                });
            });
            builder.WebHost.UseKestrelHttpsConfiguration();

            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                options.ConfigureHttpsDefaults(options =>
                    options.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate);
            });
            var app = builder.Build();

            var sampleTodos = new Todo[] {
                new(1, "Walk the dog"),
                new(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
                new(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
                new(4, "Clean the bathroom"),
                new(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
            };

            app.UseExceptionHandler(@"/Test.txt");

            app.UseStatusCodePages();
            app.UseDeveloperExceptionPage();

            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller}/{action=Index}/{id?}");
            });

            app.UseEidAuthentication();

            var todosApi = app.MapGroup("/todos");
            todosApi.MapGet("/", async (c) =>
            {
                var cert = c.Connection.ClientCertificate;
                await c.Response.WriteAsJsonAsync(sampleTodos);
                //return sampleTodos;
            });
            todosApi.MapGet("/{id}", (int id) =>
                sampleTodos.FirstOrDefault(a => a.Id == id) is { } todo
                    ? Results.Ok(todo)
                    : Results.NotFound());

            app.MapGet("/cert", () =>
            {
                // https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-8.0#create-certificates-in-powershell
                // New-SelfSignedCertificate -DnsName "localhost", "root_example.com" -CertStoreLocation "cert:\LocalMachine\My" -NotAfter (Get-Date).AddYears(20) -FriendlyName "root_example.com" -KeyUsageProperty All -KeyUsage CertSign, CRLSign, DigitalSignature
                // $mypwd = ConvertTo-SecureString -String "1234" -Force -AsPlainText
                // --Get-ChildItem -Path cert:\localMachine\my\"The thumbprint..." | Export-PfxCertificate -FilePath C:\git\root_ca_dev_damienbod.pfx -Password $mypwd
                // Get-ChildItem -Path cert:\localMachine\my\D7FC46A4D24573566364AED600079AB45ECA2ADC | Export-PfxCertificate -FilePath .\root_example.pfx -Password $mypwd
                // Export-Certificate -Cert cert:\localMachine\my\D7FC46A4D24573566364AED600079AB45ECA2ADC -FilePath root_example.crt

                // $rootcert = ( Get-ChildItem -Path cert:\LocalMachine\My\D7FC46A4D24573566364AED600079AB45ECA2ADC )
                // New-SelfSignedCertificate -certstorelocation cert:\localmachine\my -dnsname "child_a_localhost.com" -Signer $rootcert -NotAfter (Get-Date).AddYears(20) -FriendlyName "child_a_localhost.com"
                // > B5853EEEA040ED1DB3E944169EB62C190BE2FD47
                // Get-ChildItem -Path cert:\localMachine\my\B5853EEEA040ED1DB3E944169EB62C190BE2FD47 | Export-PfxCertificate -FilePath .\child_localhost.pfx -Password $mypwd
                // Export-Certificate -Cert cert:\localMachine\my\B5853EEEA040ED1DB3E944169EB62C190BE2FD47 -FilePath .\child_localhost.crt

                var properties = new AuthenticationProperties { RedirectUri = "/cert/callback" };
                return Results.Challenge(properties, [CertificateAuthenticationDefaults.AuthenticationScheme]);
            });

            app.MapGet("/login", (SignInManager<User> signInManager) =>
            {
                var properties = signInManager.ConfigureExternalAuthenticationProperties("Eid", "/login/callback");
                return Results.Challenge(properties, ["Eid"]);
            });

            app.MapGet("/login/callback", () =>
            {
                return Results.Ok();
            });
            
            app.Run();
        }
    }

    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    [JsonSerializable(typeof(Todo[]))]
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
}
