using Microsoft.AspNetCore.Authentication;

namespace DemoWebApp;

public class EidAuthenticationOptions : AuthenticationSchemeOptions
{
    //public EidAuthenticationOptions()
    //    : base("Eid") { }

    public string? SignInAsAuthenticationType { get; set; }
}