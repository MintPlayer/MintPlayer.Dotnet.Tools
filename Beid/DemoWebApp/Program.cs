using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;

namespace DemoWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            builder.Services.AddAuthentication("Eid")
                .AddScheme<EidAuthenticationOptions, EidAuthenticationHandler>("Eid", o =>
                {
                    var x = o.Events;
                });
                //.AddCertificate(o =>
                //{
                //    o.Events
                //});

            builder.Services.AddAuthorization(o =>
            {
                o.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes("Eid")
                    .RequireAuthenticatedUser()
                    .Build();
            });

            builder.Services.AddDbContext<EidContext>();

            builder.Services.AddIdentity<User, Role>()
                .AddEntityFrameworkStores<EidContext>()
                .AddDefaultTokenProviders();

            builder.WebHost.ConfigureKestrel(k =>
            {
                k.ConfigureHttpsDefaults(http =>
                {
                    http.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
                    http.AllowAnyClientCertificate();
                });
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
            //app.UseEndpoints();


            var todosApi = app.MapGroup("/todos");
            todosApi.MapGet("/", () => sampleTodos);
            todosApi.MapGet("/{id}", (int id) =>
                sampleTodos.FirstOrDefault(a => a.Id == id) is { } todo
                    ? Results.Ok(todo)
                    : Results.NotFound());

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
