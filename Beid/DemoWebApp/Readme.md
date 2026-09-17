# Belgian eID DemoWebApp

ASP.NET Core demo that authenticates users with their Belgian eID smart card via mTLS, deployed at https://eid.mintplayer.com.

After entering the card PIN in the browser, `/todos` returns the user's `PersonInfo` (firstNames, lastName, nationalNumber, validity dates) extracted from the auth certificate's subject DN.

---

## Architecture

```
Internet :443 (Traefik)
   ├── SNI: bootstrap.mintplayer.com → terminate TLS → ng-bootstrap
   ├── SNI: ... → terminate TLS → other containers
   └── SNI: eid.mintplayer.com → TCP TLS PASSTHROUGH ─┐
                                                       │ encrypted bytes
                                                       ▼
                                                  nginx-eid:443
                                                  (terminates TLS, requests client cert,
                                                   advertises only PKCS#1 v1.5 sig algs)
                                                       │ HTTP + cert in X-Forwarded-Tls-Client-Cert
                                                       ▼
                                                   .NET eid:8080
```

**Why nginx instead of letting Traefik terminate TLS for this host?** Traefik (Go's `crypto/tls`) hardcodes `rsa_pss_rsae_sha256` first in the `CertificateRequest` `signature_algorithms`. Belgian eID cards cannot sign RSA-PSS, so Firefox+NSS picks PSS, fails, and hangs. nginx (OpenSSL) lets us override the list via `ssl_conf_command SignatureAlgorithms`. See [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md) for the full diagnosis.

Other Traefik-fronted apps on the same VPS are unaffected — only the `eid.mintplayer.com` SNI gets routed via TCP passthrough.

### Traefik's exact role for this host

Traefik is an **L4 SNI switch here, not a TLS terminator**. It reads the SNI from the ClientHello and copies encrypted bytes to `nginx-eid`; it never sees plaintext, never sends a `CertificateRequest`, and never touches `signature_algorithms`. The Go TLS stack that caused the Firefox hang is genuinely not in the handshake path.

It stays in the path for exactly two reasons:

1. **It owns the ports.** `traefik-traefik-1` is the only container on the VPS binding host `:80` and `:443`; all ~19 others are reached over the `web` Docker network. Since Traefik must keep `:443` for the other sites, nginx cannot bind it, so eid's bytes have to arrive through Traefik. This is why "drop Traefik for this one app" has no better shape than what's here — the only alternative is making nginx the edge for *every* site.
2. **It supplies the certificate** via ACME + `traefik-certs-dumper` (below). Giving nginx its own certbot wouldn't remove the dependency: HTTP-01 needs `:80`, which Traefik owns, so the challenge would have to be routed through Traefik anyway.

> **Important — no HTTP router for this host.** The `docker-compose.yml` deliberately defines *only* a TCP TLS-passthrough router. Adding an HTTP router with `tls.certresolver` on the same `websecure` entrypoint and SNI conflicts with the TCP router: Traefik ends up terminating TLS itself and forwarding plain HTTP to nginx, which responds with `400 Bad Request — The plain HTTP request was sent to HTTPS port`. The Let's Encrypt cert is still maintained — see "First-time cert acquisition" below.

---

## Files

| File | Purpose |
|---|---|
| `Program.cs` | The .NET app. Local dev: Kestrel terminates TLS directly. Prod: reads the leaf cert (URL-encoded PEM) from `X-Forwarded-Tls-Client-Cert`. |
| `docker-compose.yml` | Two services: `nginx-eid` (TLS+mTLS terminator, exposed via Traefik TCP passthrough) and `eid` (the .NET app, internal-only). |
| `nginx.conf` | nginx config. Terminates TLS 1.2, requests client cert with `optional_no_ca`, sets `ssl_conf_command SignatureAlgorithms RSA+SHA256:RSA+SHA384:RSA+SHA512` so Firefox doesn't try PSS, forwards cert to `eid:8080`. |
| `traefik-dynamic-config/belgian-eid-cas.pem` | Combined PEM of four Belgian CAs. Used by nginx as `ssl_client_certificate` — the names go into the `CertificateRequest` so browsers know which cert to offer. **Load-bearing**: nginx won't start without it. See "Regenerating the CA bundle" below. |
| `traefik-dynamic-config/*.pem` (the other four) | The individual CAs the bundle is concatenated from. Not deployed; kept as the bundle's sources. |
| `Dockerfile` | Builds the .NET image published to `ghcr.io/mintplayer/mintplayer-dotnet-tools-eid`. |
| `.github/workflows/eid-deploy.yml` | Builds the Docker image on every push to `master` (paths `Beid/DemoWebApp/**`), then SSHes to the VPS and runs `docker compose pull && up -d`. |

### Companion docs

- [`DEPLOYMENT.md`](DEPLOYMENT.md) — **the server map**: exactly what must exist where on the VPS, who puts it there, what breaks if it's missing, and how to rebuild from scratch. Start here for anything operational.
- [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md) — root-cause analysis of why Firefox hangs after PIN entry, and why the fix has to be at the TLS-terminator layer.
- [`PRD-firefox-mtls-fix.md`](PRD-firefox-mtls-fix.md) — earlier PRD that addressed the *first* Firefox symptom (no PIN prompt at all) by adding `caFiles` and ALPN restrictions to Traefik. Those fixes are now superseded for this host but were correct for that earlier failure mode.
- [`CLIENT-CERT-FORWARDING.md`](CLIENT-CERT-FORWARDING.md) — explains the `X-Forwarded-Tls-Client-Cert` header flow. Written when Traefik was the terminator; the .NET-side reading logic is unchanged with nginx, only the header-value format differs (URL-encoded PEM vs URL-encoded base64 DER).
- [`Create-Certificate.md`](Create-Certificate.md) — local-dev cert generation notes.

---

## Regenerating the CA bundle

`traefik-dynamic-config/belgian-eid-cas.pem` is a plain concatenation, in this order, of the four individual PEMs sitting beside it:

```bash
cd Beid/DemoWebApp/traefik-dynamic-config
cat belgium-root-ca4.pem citizen-ca.pem belgium-root-ca6.pem citizen-ca-ecc.pem > belgian-eid-cas.pem
```

The four CAs it contains:

| File | Subject | Covers |
|---|---|---|
| `belgium-root-ca4.pem` | `C=BE, CN=Belgium Root CA4` | older RSA cards |
| `citizen-ca.pem` | `CN=Citizen CA, serialNumber=201701` | issuer of the 2017-era RSA test card |
| `belgium-root-ca6.pem` | `CN=Belgium Root CA6` (BOSA) | newer cards |
| `citizen-ca-ecc.pem` | `CN=Citizen CA, serialNumber=202002` | newer ECC (applet ≥ 1.8) cards |

Order doesn't matter to OpenSSL; keep it stable so diffs stay readable. Every CA in this file is advertised by name in the `CertificateRequest`, which is how the browser decides the eID card is relevant — a card whose issuer is missing here will simply not be offered.

If you download a CA from `repository.eid.belgium.be` in DER form (`.cer`/`.crt`), convert it before concatenating:

```bash
openssl x509 -inform der -in citizen-ca.cer -out citizen-ca.pem
```

> **Directory name is historical.** `traefik-dynamic-config/` dates from when Traefik terminated TLS and read its own mTLS config from there. It now holds **only** nginx's client-CA material. The name is misleading but the path is live — `docker-compose.yml` mounts `./traefik-dynamic-config/belgian-eid-cas.pem` and `eid-deploy.yml` writes into it — so don't delete `/var/www/eid/traefik-dynamic-config` on the VPS. It is **not** the same as the stale `/var/www/traefik/dynamic/` (see below).

### Stale files on the VPS (safe to delete by hand)

`/var/www/traefik/dynamic/` still contains the Traefik-side mTLS config from before the nginx switchover. It is inert — verified 2026-09-17 that no container label and no compose file anywhere on the VPS references `tls.options` or `mtls`, and a TCP passthrough router cannot take a `tls.options` reference at all. Note the option is named `mtls`, **not** `default`, so it is not silently applying to other sites.

```
/var/www/traefik/dynamic/tls.yml              # dead: the mtls TLS option, referenced by nothing
/var/www/traefik/dynamic/belgium-root-ca4.pem # only existed for tls.yml's caFiles
/var/www/traefik/dynamic/citizen-ca.pem       # ditto
/var/www/traefik/dynamic/certs/               # three more orphaned PEMs (Mar 2026)
```

Delete the **files**, keep the **directory** — Traefik runs with `--providers.file.directory=/etc/traefik/dynamic` and an empty directory is fine (all routing comes from the Docker provider), but a missing mount source plus `--providers.file.watch=true` makes it noisy.

## Local development

`dotnet run` against the `http` launch profile — Kestrel listens on `https://localhost:5050` with `ClientCertificateMode.RequireCertificate` and a 2-minute handshake timeout (so PIN entry doesn't time out). See `Properties/launchSettings.json`.

> **Note:** Firefox + Belgian eID does **not** work locally either, for the same PSS reason — Schannel in server mode also advertises PSS first. Use Chrome for local testing (it goes Schannel→MSCAPI→eID CSP, a different code path that filters PSS out).

---

## VPS one-time setup

The GitHub Action handles app deployment on every push. The bits below are **one-time** infrastructure changes you do once on the VPS.

### 1. Add `traefik-certs-dumper` sidecar to Traefik

nginx needs a TLS cert. Traefik continues to obtain (and renew) the Let's Encrypt cert for `eid.mintplayer.com` via its built-in ACME loop — see "First-time cert acquisition" below for the bootstrap. The cert lives inside Traefik's `acme.json`; the `traefik-certs-dumper` sidecar watches that file and writes out plain `certificate.pem` / `privatekey.pem` files that nginx-eid can mount.

In `/var/www/traefik/docker-compose.yml`, add alongside the `traefik` service:

```yaml
  certs-dumper:
    image: ldez/traefik-certs-dumper:latest
    command:
      - file
      - --version=v3
      - --watch
      - --source=/letsencrypt/acme.json
      - --dest=/dumped
      - --domain-subdir=true
    volumes:
      - letsencrypt:/letsencrypt:ro
      - /var/www/traefik/dumped-certs:/dumped
    restart: unless-stopped
```

### 2. Bootstrap the cert dump (run once)

Run a one-shot dump of the existing cert before starting the long-running watcher. This avoids `nginx-eid` crash-looping on first deploy:

```bash
cd /var/www/traefik
docker compose run --rm certs-dumper file --version=v3 --source=/letsencrypt/acme.json --dest=/dumped --domain-subdir=true
```

Verify the result:

```bash
ls -la /var/www/traefik/dumped-certs/eid.mintplayer.com/
# expected:
#   certificate.pem
#   privatekey.pem
```

`nginx-eid` mounts that directory directly (`docker-compose.yml:24`).

### 3. Start the long-running watcher

```bash
cd /var/www/traefik
docker compose up -d certs-dumper
```

Confirm it's running:

```bash
docker compose ps certs-dumper
# STATUS should be "Up"
```

This watches `acme.json` and re-dumps `certificate.pem` / `privatekey.pem` automatically whenever Traefik renews the cert.

### 4. Reload nginx-eid when the cert renews

Let's Encrypt certs are valid for 90 days; Traefik renews ~30 days before expiry. The dumper writes new files immediately, but nginx loads its cert at startup — it needs `nginx -s reload` to pick up the new file. Pick **one** of:

**Option A — host cron** (simplest if you're comfortable with cron). Debian has cron running by default (`systemctl status cron`). As root:

```bash
sudo crontab -e
# add this line:
0 4 * * * docker exec nginx-eid nginx -s reload >/dev/null 2>&1
```

Daily at 04:00. Renewal happens once every ~60 days, so daily reload is overkill but harmless.

> The container name `nginx-eid` is pinned via `container_name:` in `docker-compose.yml`. Without that pin, Compose would auto-name it `<project>-nginx-eid-1` (e.g. `eid-nginx-eid-1`), and the line above wouldn't work.

**Option B — ofelia sidecar** (no host changes). Add to this app's `docker-compose.yml`:

```yaml
  ofelia:
    image: mcuadros/ofelia:latest
    command: daemon --docker
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    networks:
      - web
    restart: unless-stopped
```

And add labels to `nginx-eid`:

```yaml
      - "ofelia.enabled=true"
      - "ofelia.job-exec.reload-nginx.schedule=0 0 4 * * *"
      - "ofelia.job-exec.reload-nginx.command=nginx -s reload"
```

Fully declarative, no host crontab.

### 5. Confirm `eid.mintplayer.com` resolves to your VPS

DNS A record → VPS IP. (Already in place if the previous Traefik-terminated setup was working.)

---

## First-time cert acquisition (only if `acme.json` is empty for this host)

The `docker-compose.yml` deliberately defines no HTTP router for `eid.mintplayer.com` (an HTTP router with `tls.certresolver` would conflict with the TCP-passthrough router and break TLS termination — see the warning at the top of this README). That means Traefik has nothing to *trigger* an ACME challenge for this host on a fresh VPS.

If `eid.mintplayer.com` was previously served via Traefik (as it was here before the nginx switchover), the cert is already in `/letsencrypt/acme.json` and Traefik's renewal loop keeps it fresh — no action needed. Skip this section.

For a **brand-new VPS** with no prior cert for the host:

1. Temporarily add to your eID `docker-compose.yml`, alongside the TCP router labels:

   ```yaml
         - "traefik.http.routers.eid-bootstrap.rule=Host(`eid.mintplayer.com`)"
         - "traefik.http.routers.eid-bootstrap.entrypoints=web"
         - "traefik.http.routers.eid-bootstrap.tls.certresolver=letsencrypt"
         - "traefik.http.routers.eid-bootstrap.service=eid-bootstrap"
         - "traefik.http.services.eid-bootstrap.loadbalancer.server.port=80"
   ```

   Note the entrypoint is `web` (port 80), **not** `websecure` — this avoids conflicting with the TCP passthrough router on 443. Traefik still does HTTP-01 over port 80, so the cert gets fetched.

2. `docker compose up -d`. Watch `/var/www/traefik/dumped-certs/eid.mintplayer.com/` appear with `certificate.pem` and `privatekey.pem` (a few seconds after Traefik fetches via ACME).

3. Remove those four labels and `docker compose up -d` again. The cert stays in `acme.json` from now on; Traefik's renewal loop keeps it current.

---

## Verifying

```bash
# The one check that matters: the advertised schemes must contain NO rsa_pss_*.
echo | openssl s_client -connect eid.mintplayer.com:443 -servername eid.mintplayer.com \
    -tls1_2 -trace 2>&1 | grep -E "Requested Signature Algorithms|Peer signature type"
```

Verified output (2026-09-17):

```
Requested Signature Algorithms: RSA+SHA256:RSA+SHA384:RSA+SHA512:ECDSA+SHA256:ECDSA+SHA384:ECDSA+SHA512
Peer signature type: rsa_pkcs1_sha256
```

Six schemes, zero RSA-PSS. Compare with the ten-entry Go list in [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md), which has `rsa_pss_rsae_sha256` **first** — seeing that list again means Traefik has taken TLS termination back over and Firefox will hang.

```bash
# Endpoint sanity, no card present — expect 401 with a valid server cert:
curl -s -o /dev/null -w "http=%{http_code}\n" https://eid.mintplayer.com/todos

# Cert freshness (on the VPS) — Traefik's ACME loop keeps this current:
openssl x509 -in /var/www/traefik/dumped-certs/eid.mintplayer.com/certificate.pem \
    -noout -subject -enddate -issuer
```

Browser test: insert eID, navigate to `https://eid.mintplayer.com/todos`, enter PIN. Should return JSON like:

```json
{"notBefore":"...","notAfter":"...","firstNames":"...","lastName":"...","nationalNumber":"...","country":"BE"}
```

In **both** Chrome and Firefox.
