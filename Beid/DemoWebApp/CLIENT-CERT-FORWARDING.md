# Client Certificate Forwarding (Belgian eID through nginx)

## How it works

The Belgian eID card signs the TLS handshake directly, so the client certificate must be received by whoever terminates TLS. In our setup that is **nginx** (`nginx-eid`), not Traefik and not the .NET app.

> **Historical note.** Until 2026-04-28 Traefik terminated TLS for this host and forwarded the cert via its `passTLSClientCert` middleware as URL-encoded **base64 DER**. That no longer applies: Traefik now does TCP/SNI passthrough only, nginx terminates, and the header carries URL-encoded **PEM**. The header *name* is unchanged. See [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md) for why termination had to move off Traefik.

### The flow

```
Browser (eID card)
   │ TLS 1.2 + client cert
   ▼
Traefik :443 ──── TCP passthrough (HostSNI) ────► nginx-eid :443
                  never terminates, never sees        │ terminates TLS, requests client cert
                  plaintext, no CertificateRequest    │ HTTP + X-Forwarded-Tls-Client-Cert
                                                      ▼
                                                  .NET eid:8080
```

1. **Browser to Traefik.** The browser connects to `eid.mintplayer.com:443`. Traefik reads only the SNI from the ClientHello and copies the encrypted bytes onward. It performs no handshake for this host.

2. **nginx terminates.** nginx presents the Let's Encrypt cert, requests a client certificate (`ssl_verify_client optional_no_ca`), and advertises the CA names from `ssl_client_certificate` so the browser knows to offer the eID card. The user enters their PIN and the browser sends the signed certificate.

3. **nginx to the .NET app.** nginx proxies plain HTTP to `eid:8080`, placing the leaf certificate in the `X-Forwarded-Tls-Client-Cert` header as URL-encoded PEM (`$ssl_client_escaped_cert`).

4. **The .NET app reads the header.** The `CertificateForwarding` middleware decodes it and sets `HttpContext.Connection.ClientCertificate`, so endpoint code is identical to the local-development case.

### The three pieces of config that make it work

#### 1. nginx TLS + mTLS (`nginx.conf`)

```nginx
ssl_protocols       TLSv1.2;
ssl_conf_command    SignatureAlgorithms RSA+SHA256:RSA+SHA384:RSA+SHA512:ECDSA+SHA256:ECDSA+SHA384:ECDSA+SHA512;
ssl_verify_client       optional_no_ca;
ssl_client_certificate  /etc/nginx/ca/eid-cas.pem;
```

`ssl_conf_command SignatureAlgorithms` is the whole reason nginx is in this stack — it advertises PKCS#1 v1.5 and ECDSA but **never RSA-PSS**, which Go's TLS (Traefik) cannot express. `optional_no_ca` requests the cert without rejecting its absence, leaving the 401 decision to the app.

#### 2. Passing the cert to the app (`nginx.conf`)

```nginx
proxy_set_header X-Forwarded-Tls-Client-Cert $ssl_client_escaped_cert;
```

`$ssl_client_escaped_cert` is the **leaf only**, URL-encoded PEM — including the `-----BEGIN CERTIFICATE-----` armour, with newlines encoded as `%0A`.

#### 3. ASP.NET Core CertificateForwarding (`Program.cs:112-121`)

```csharp
builder.Services.AddCertificateForwarding(options =>
{
    options.CertificateHeader = "X-Forwarded-Tls-Client-Cert";
    options.HeaderConverter = (headerValue) =>
    {
        if (string.IsNullOrEmpty(headerValue)) return null!;
        var pem = Uri.UnescapeDataString(headerValue);
        return X509Certificate2.CreateFromPem(pem);
    };
});
```

Key details about the header format:

- Use `Uri.UnescapeDataString`, **not** `HttpUtility.UrlDecode`. The latter turns `+` into a space, and `+` occurs inside base64 PEM bodies.
- nginx sends only the leaf, so there is no chain to split on — unlike the old Traefik format, which comma-separated the chain and required taking element `[0]`.
- `X509Certificate2.CreateFromPem` parses the armoured text directly; no `Convert.FromBase64String` step.
- Only the leaf carries the user's personal information (national number, names, validity).

## Trust model — what `belgian-eid-cas.pem` is and isn't

It is a **CA trust list**, not a certificate chain. The four CAs in it are unrelated to each other (two roots and two issuing CAs, spanning the RSA and ECC card generations); they are not a path from leaf to root.

nginx uses it for two distinct purposes, and here only the first is active:

1. **Advertising acceptable CA names.** The subjects in this file are listed in the `CertificateRequest`, which is how the browser decides the eID card is relevant and prompts for the PIN. A card whose issuer is absent here is simply never offered. This is the job the file is actually doing.
2. **Verifying the client chain.** This is what `ssl_client_certificate` normally enables — but `ssl_verify_client optional_no_ca` deliberately switches it off. nginx requests the cert, accepts whatever arrives without validating its issuer, sets `$ssl_client_verify` to `NONE`, and forwards it. That is intentional: the app, not nginx, decides whether a given endpoint requires authentication.

> **Open question — where is the issuer actually verified?** With nginx not verifying, the check has to happen in the app. `Program.cs:33-66` uses ASP.NET Core's `AddCertificate` with `OnCertificateValidated` calling `context.Success()`, and both `CustomTrustStore` and `ChainTrustValidationMode` are commented out. Separately, `EidAuthenticationHandler.ValidateX509Certificate` does pin the issuer by thumbprint — but that handler is not wired up (`app.UseEidAuthentication()` at `Program.cs:160` is commented out, and the scheme selected on line 33 is the certificate one). Worth confirming deliberately before treating a successful `/todos` response as proof the presenting card was issued by the Belgian government. Note also that the pinned thumbprints in that dead handler cover only Citizen CA 201701 and Belgium Root CA4 — not the CA6/ECC pair the bundle advertises.

## Why this approach works

The endpoint only needs the **public certificate** to extract the subject fields. The private key never leaves the eID card — it is used solely to sign the TLS `CertificateVerify`, which nginx validates during the handshake.

## Development vs Production

- **Development** (local): Kestrel terminates TLS directly with `ClientCertificateMode.RequireCertificate`, so the certificate arrives on `Connection.ClientCertificate` from the TLS connection. A 2-minute `HandshakeTimeout` allows time for PIN entry. Note that Firefox does **not** work locally — Schannel in server mode advertises RSA-PSS first, the same root cause nginx exists to fix. Use Chrome for local mTLS testing.
- **Production**: the app runs on plain HTTP behind nginx. `CertificateForwarding` reads the header and sets `Connection.ClientCertificate`. Endpoint code is identical in both cases.
