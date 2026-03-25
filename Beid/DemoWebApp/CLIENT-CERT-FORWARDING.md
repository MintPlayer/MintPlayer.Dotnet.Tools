# Client Certificate Forwarding (Belgian eID through Traefik)

## How it works

The Belgian eID card signs the TLS handshake directly, so the client certificate must be received by whoever terminates TLS. In our setup that's Traefik, not the .NET app.

### The flow

```
Browser (eID card)  --TLS 1.2 + client cert-->  Traefik  --HTTP + header-->  .NET app
```

1. **Browser to Traefik**: The browser connects to `eid.mintplayer.com:443`. Traefik terminates TLS (with a Let's Encrypt cert) and requests a client certificate. The browser shows the eID certificate dialog, the user enters their PIN, and the browser sends the signed certificate.

2. **Traefik extracts the cert**: The `passTLSClientCert` middleware takes the client certificate and puts it in the `X-Forwarded-Tls-Client-Cert` HTTP header as URL-encoded base64 DER (comma-separated if there's a chain).

3. **Traefik to .NET app**: Traefik forwards the HTTP request (plain, no TLS) to the container, with the cert in the header.

4. **.NET app reads the header**: The `CertificateForwarding` middleware reads `X-Forwarded-Tls-Client-Cert`, decodes it, and sets `HttpContext.Connection.ClientCertificate` so the endpoint code works identically to the local development case.

### The three pieces of config that make it work

#### 1. Traefik TLS options (`traefik-dynamic-config/tls.yml`)

Request a client cert and restrict to TLS 1.2 (Belgian eID cards don't support TLS 1.3 client certificate authentication):

```yaml
tls:
  options:
    mtls:
      minVersion: VersionTLS12
      maxVersion: VersionTLS12
      clientAuth:
        clientAuthType: RequestClientCert
```

This file is placed on the VPS at `/var/www/traefik/dynamic/tls.yml` by the deploy workflow. Traefik picks it up via its file provider (`--providers.file.directory=/etc/traefik/dynamic`).

The `mtls` TLS option is only referenced by the `eid` router. All other sites continue using the default TLS options (no client cert requested).

#### 2. Docker Compose labels (`docker-compose.yml`)

Attach the TLS options and cert forwarding middleware to the eid router:

```yaml
labels:
  - "traefik.http.routers.eid.tls.options=mtls@file"
  - "traefik.http.middlewares.eid-clientcert.passtlsclientcert.pem=true"
  - "traefik.http.routers.eid.middlewares=eid-clientcert"
```

#### 3. ASP.NET Core CertificateForwarding (`Program.cs`)

Decode the header and set `Connection.ClientCertificate`:

```csharp
builder.Services.AddCertificateForwarding(options =>
{
    options.CertificateHeader = "X-Forwarded-Tls-Client-Cert";
    options.HeaderConverter = (headerValue) =>
    {
        var decoded = Uri.UnescapeDataString(headerValue);
        var leafCertBase64 = decoded.Split(',')[0]; // first cert in chain
        var certBytes = Convert.FromBase64String(leafCertBase64);
        return X509CertificateLoader.LoadCertificate(certBytes);
    };
});
```

Key details about the header format:
- Traefik URL-encodes the base64 DER certificate (use `Uri.UnescapeDataString`, not `HttpUtility.UrlDecode`, because `+` is a valid base64 character)
- If the client sends a certificate chain, Traefik comma-separates the certificates (leaf first)
- Only the leaf certificate (first in the chain) contains the user's personal information

## Why this approach works

The endpoint only needs the **public certificate** to extract the user's name, national number, and other subject fields. The private key never leaves the eID card; it is only used to sign the TLS handshake with Traefik.

## Development vs Production

- **Development** (local): Kestrel handles TLS directly with `ClientCertificateMode.RequireCertificate`. The certificate is available on `Connection.ClientCertificate` via the TLS connection. A 2-minute `HandshakeTimeout` is configured to allow time for PIN entry.
- **Production** (Docker behind Traefik): The app runs on HTTP. The `CertificateForwarding` middleware reads the certificate from the `X-Forwarded-Tls-Client-Cert` header and sets `Connection.ClientCertificate`. The endpoint code is identical in both cases.
