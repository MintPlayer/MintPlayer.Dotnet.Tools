using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace DemoWebApp;

public class EidAuthenticationEvents { }

public class EidAuthenticationOptions : AuthenticationSchemeOptions
{
    public EidAuthenticationOptions()
    {
    }
    public string SignInAsAuthenticationType { get; set; } = null!;
}

public class EidAuthenticationHandler : AuthenticationHandler<EidAuthenticationOptions>
{
    public EidAuthenticationHandler(IOptionsMonitor<EidAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<object> CreateEventsAsync()
    {
        var ev = new EidAuthenticationEvents();
        return Task.FromResult<object>(ev);
    }

    protected override async Task InitializeHandlerAsync()
    {
        await base.InitializeHandlerAsync();
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        try
        {
            await base.HandleChallengeAsync(properties);
        }
        catch (Exception ex)
        {
        }
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        await base.HandleForbiddenAsync(properties);
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var x509 = await Context.Connection.GetClientCertificateAsync();
        if (x509 != null)
        {
            var identity = ValidateX509Certificate(x509);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }
        else
        {
            return AuthenticateResult.Fail(string.Empty);
        }
    }

    private ClaimsIdentity ValidateX509Certificate(X509Certificate2 x509)
    {
        var chain = new X509Chain(true);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
        chain.Build(x509);

        var citizenCertificate = chain.ChainElements[0].Certificate;

        if (citizenCertificate.NotAfter < DateTime.Now)
            throw new Exception("The citizen certificate is not (longer) valid.");

        //TODO verify if citizen certificate has not been revoked

        if (chain.ChainElements[1].Certificate.Thumbprint != "74CC6E5559FFD7C2DD0526C0C21593C56C9384F3")
            throw new Exception("Invalid Citizen CA certificate.");

        if (chain.ChainElements[2].Certificate.Thumbprint != "51CCA0710AF7733D34ACDC1945099F435C7FC59F")
            throw new Exception("Invalid Belgium Root CA certificate.");

        var firstName = Regex.Match(citizenCertificate.Subject, "G=([^,]*),").Groups[1].Value;
        var lastName = Regex.Match(citizenCertificate.Subject, "SN=([^,]*),").Groups[1].Value;
        var nationalRegisterIdentificationNumber = Regex.Match(citizenCertificate.Subject, "SERIALNUMBER=([^,]*),").Groups[1].Value;
        var nationality = Regex.Match(citizenCertificate.Subject, "C=([^,]*)").Groups[1].Value;

        // Based on information of: https://www.ksz-bcss.fgov.be/nl/bcss/page/content/websites/belgium/services/docutheque/technical_faq/faq_5.html
        var isMale = int.Parse(nationalRegisterIdentificationNumber.Substring(6, 3)) % 2 == 1;

        var identity = new ClaimsIdentity(Options.SignInAsAuthenticationType);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, firstName + " " + lastName, null, Scheme.Name));
        identity.AddClaim(new Claim(ClaimTypes.GivenName, firstName));
        identity.AddClaim(new Claim(ClaimTypes.Name, lastName));
        identity.AddClaim(new Claim(ClaimTypes.Gender, isMale ? "M" : "F"));
        identity.AddClaim(new Claim(ClaimTypes.Country, nationality));

        return identity;
    }
}

//public class EidAuthenticationMiddleware : AuthenticationMiddleware //<EidAuthenticationOptions>
//{
//    public EidAuthenticationMiddleware(RequestDelegate next, IAuthenticationSchemeProvider schemes, EidAuthenticationOptions options)
//        : base(next, schemes)
//    {
//        //if (string.IsNullOrEmpty( .SignInAsAuthenticationType))
//        //{
//        //    options.SignInAsAuthenticationType = app.GetDefaultSignInAsAuthenticationType();
//        //}
//    }

//    override 

//    //protected override AuthenticationHandler<EidAuthenticationOptions> CreateHandler()
//    //{
//    //    return new EidAuthenticationHandler();
//    //}
//}