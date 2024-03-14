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
using System.Net;

namespace DemoWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback = (_, __, ___, ____) => true;

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
                    //o.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                    o.AllowedCertificateTypes = CertificateTypes.All;
                    //o.ValidateCertificateUse = false;
                    //o.ValidateValidityPeriod = false;
                    //o.ChainTrustValidationMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.System;
                    //o.ChainTrustValidationMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                    o.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                    //o.CustomTrustStore = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection
                    //{

                    //};

                    o.Events = new CertificateAuthenticationEvents
                    {
                        OnChallenge = async (context) =>
                        {

                        },
                        OnAuthenticationFailed = async (context) =>
                        {

                        },
                        OnCertificateValidated = (context) =>
                        {
                            //var thb = context.ClientCertificate?.Thumbprint;
                            //context.Principal = new ClaimsPrincipal(new ClaimsIdentity([], context.Scheme.Name));
                            context.Success();
                            return Task.CompletedTask;
                        }
                    };
                });
                //.AddScheme<EidAuthenticationOptions, EidAuthenticationHandler>("Eid", o =>
                //{
                //    var x = o.Events;
                //});

            builder.Services.AddAuthorization(o =>
            {
                o.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(CertificateAuthenticationDefaults.AuthenticationScheme)
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
                    http.ClientCertificateValidation = (_, __, ___) =>
                    {
                        return true;
                    };
                    http.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                });
            });
            builder.WebHost.UseKestrelHttpsConfiguration();

            var app = builder.Build();

            var sampleTodos = new Todo[] {
                new(1, "Walk the dog"),
                new(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
                new(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
                new(4, "Clean the bathroom"),
                new(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
            };

            app.UseExceptionHandler(@"/Test.txt");
            app.UseHttpsRedirection();
            app.UseStatusCodePages();
            app.UseDeveloperExceptionPage();

            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                //endpoints.MapControllerRoute(
                //    name: "default",
                //    pattern: "{controller}/{action=Index}/{id?}");
                endpoints.MapControllers();
            });

            //app.UseEidAuthentication();

            app.Use(async (context, next) =>
            {
                var cert = context.Connection.ClientCertificate;

                await next();
            });

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
                // $rootpassword = ConvertTo-SecureString -String "1234" -Force -AsPlainText
                // $rootcert = New-SelfSignedCertificate -Type Custom -KeySpec Signature -Subject "CN=P2SRootCert" -KeyExportPolicy Exportable -HashAlgorithm sha256 -KeyLength 4096 -CertStoreLocation "Cert:\CurrentUser\My" -KeyUsageProperty Sign -KeyUsage CertSign -NotAfter(Get-Date).AddYears(5)
                // $rootcertpath = 'Cert:\CurrentUser\My\' + $rootcert.Thumbprint
                // $childcert = New-SelfSignedCertificate -Type Custom -KeySpec Signature -Subject "CN=P2SChildCert" -KeyExportPolicy Exportable -HashAlgorithm sha256 -KeyLength 2048 -NotAfter(Get-Date).AddMonths(24) -CertStoreLocation "Cert:\CurrentUser\My" -Signer $rootcert -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.2")
                // $childcertpath = 'Cert:\CurrentUser\My\' + $childcert.Thumbprint
                // Export-Certificate -Cert $rootcertpath -FilePath root_example.crt -Password $rootpassword
                // Export-Certificate -Cert $childcertpath -FilePath child_example.crt


                // Also todo:
                // Download + install EID viewer
                // Insert card => Under Certificates tab => Right-click Belgium Root CA4 => Gedetailleerde informatie
                // => Certificaat installeren


                var properties = new AuthenticationProperties { RedirectUri = "/cert/callback" };
                return Results.Challenge(properties, [CertificateAuthenticationDefaults.AuthenticationScheme]);
            }).AllowAnonymous();

            app.MapGet("/login", (SignInManager<User> signInManager) =>
            {
                var properties = signInManager.ConfigureExternalAuthenticationProperties(CertificateAuthenticationDefaults.AuthenticationScheme, "/login/callback");
                return Results.Challenge(properties, [CertificateAuthenticationDefaults.AuthenticationScheme]);
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
