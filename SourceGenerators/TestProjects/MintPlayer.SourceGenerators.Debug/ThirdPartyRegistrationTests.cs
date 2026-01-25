using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

// Simulate registering third-party types at the assembly level
// These types could come from NuGet packages where you can't add [Register] to the class itself

// Pattern 3: Assembly-level self-registration (implementation = service type)
[assembly: Register(typeof(ThirdParty.ExternalService), ServiceLifetime.Scoped)]

// Pattern 4: Assembly-level with interface + implementation
[assembly: Register(typeof(ThirdParty.IApiClient), typeof(ThirdParty.ApiClient), ServiceLifetime.Singleton, "AddThirdPartyServices")]

// Simulated third-party library namespace (imagine this comes from a NuGet package)
namespace ThirdParty
{
    /// <summary>
    /// Imagine this interface is defined in a third-party NuGet package.
    /// </summary>
    public interface IExternalService
    {
        string GetData();
    }

    /// <summary>
    /// Imagine this class is defined in a third-party NuGet package.
    /// You can't add [Register] to it because you don't own the source code.
    /// </summary>
    public class ExternalService : IExternalService
    {
        public string GetData() => "Data from external service";
    }

    /// <summary>
    /// Another third-party interface.
    /// </summary>
    public interface IApiClient
    {
        Task<string> FetchAsync(string endpoint);
    }

    /// <summary>
    /// Another third-party implementation.
    /// </summary>
    public class ApiClient : IApiClient
    {
        public Task<string> FetchAsync(string endpoint) => Task.FromResult($"Response from {endpoint}");
    }
}
