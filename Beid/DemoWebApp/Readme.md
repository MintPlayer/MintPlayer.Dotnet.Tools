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

---

## Files

| File | Purpose |
|---|---|
| `Program.cs` | The .NET app. Local dev: Kestrel terminates TLS directly. Prod: reads the leaf cert (URL-encoded PEM) from `X-Forwarded-Tls-Client-Cert`. |
| `docker-compose.yml` | Two services: `nginx-eid` (TLS+mTLS terminator, exposed via Traefik TCP passthrough) and `eid` (the .NET app, internal-only). |
| `nginx.conf` | nginx config. Terminates TLS 1.2, requests client cert with `optional_no_ca`, sets `ssl_conf_command SignatureAlgorithms RSA+SHA256:RSA+SHA384:RSA+SHA512` so Firefox doesn't try PSS, forwards cert to `eid:8080`. |
| `traefik-dynamic-config/belgian-eid-cas.pem` | Combined PEM of `Belgium Root CA4` + `Citizen CA serialNumber=201701`. Used by nginx as `ssl_client_certificate` (the names go into the `CertificateRequest` so browsers know which cert to offer). |
| `Dockerfile` | Builds the .NET image published to `ghcr.io/mintplayer/mintplayer-dotnet-tools-eid`. |
| `.github/workflows/eid-deploy.yml` | Builds the Docker image on every push to `master` (paths `Beid/DemoWebApp/**`), then SSHes to the VPS and runs `docker compose pull && up -d`. |

### Companion docs

- [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md) — root-cause analysis of why Firefox hangs after PIN entry, and why the fix has to be at the TLS-terminator layer.
- [`PRD-firefox-mtls-fix.md`](PRD-firefox-mtls-fix.md) — earlier PRD that addressed the *first* Firefox symptom (no PIN prompt at all) by adding `caFiles` and ALPN restrictions to Traefik. Those fixes are now superseded for this host but were correct for that earlier failure mode.
- [`CLIENT-CERT-FORWARDING.md`](CLIENT-CERT-FORWARDING.md) — explains the `X-Forwarded-Tls-Client-Cert` header flow. Written when Traefik was the terminator; the .NET-side reading logic is unchanged with nginx, only the header-value format differs (URL-encoded PEM vs URL-encoded base64 DER).
- [`Create-Certificate.md`](Create-Certificate.md) — local-dev cert generation notes.

---

## Local development

`dotnet run` against the `http` launch profile — Kestrel listens on `https://localhost:5050` with `ClientCertificateMode.RequireCertificate` and a 2-minute handshake timeout (so PIN entry doesn't time out). See `Properties/launchSettings.json`.

> **Note:** Firefox + Belgian eID does **not** work locally either, for the same PSS reason — Schannel in server mode also advertises PSS first. Use Chrome for local testing (it goes Schannel→MSCAPI→eID CSP, a different code path that filters PSS out).

---

## VPS one-time setup

The GitHub Action handles app deployment on every push. The bits below are **one-time** infrastructure changes you do once on the VPS.

### 1. Add `traefik-certs-dumper` sidecar to Traefik

The eID app's nginx needs a TLS cert. Traefik already obtains one via Let's Encrypt (because of the phantom HTTP router in this app's docker-compose), but it lives inside Traefik's `acme.json`. The `traefik-certs-dumper` sidecar watches `acme.json` and writes the cert + key out as plain files that nginx-eid can mount.

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

Then `cd /var/www/traefik && docker compose up -d certs-dumper`.

### 2. Bootstrap the cert dump (run once)

The watcher only writes new files when `acme.json` changes. To force an immediate one-time dump of the existing cert (avoids `nginx-eid` crash-looping on first deploy):

```bash
cd /var/www/traefik
docker compose run --rm certs-dumper file --version=v3 --source=/letsencrypt/acme.json --dest=/dumped --domain-subdir=true
```

Verify the result:

```bash
ls -la /var/www/traefik/dumped-certs/letsencrypt/eid.mintplayer.com/
# expected:
#   certificate.crt
#   privatekey.key
```

`nginx-eid` mounts that directory directly (`docker-compose.yml:24`).

### 3. Reload nginx-eid when the cert renews

Let's Encrypt certs are valid for 90 days; Traefik renews ~30 days before expiry. The dumper writes new files immediately, but nginx loads its cert at startup — it needs `nginx -s reload` to pick up the new file. Pick **one** of:

**Option A — host cron** (simplest if you're comfortable with cron). Debian has cron running by default (`systemctl status cron`). As root:

```bash
sudo crontab -e
# add this line:
0 4 * * * docker exec nginx-eid nginx -s reload >/dev/null 2>&1
```

Daily at 04:00. Renewal happens once every ~60 days, so daily reload is overkill but harmless.

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

### 4. Confirm `eid.mintplayer.com` resolves to your VPS

DNS A record → VPS IP. (Already in place if the previous Traefik-terminated setup was working.)

---

## Verifying

```bash
# From outside the VPS — should show TLS 1.2, ALPN http/1.1, Acceptable CA names:
echo Q | openssl s_client -connect eid.mintplayer.com:443 -tls1_2 2>&1 | grep -E "(CA names|ALPN|Cipher|Protocol|signature)"

# From outside, with -trace, the CertificateRequest signature_algorithms should
# now start with rsa_pkcs1_sha256 (NOT rsa_pss_rsae_sha256):
echo Q | openssl s_client -connect eid.mintplayer.com:443 -tls1_2 -trace 2>&1 | grep -A 15 "signature_algorithms"
```

Browser test: insert eID, navigate to `https://eid.mintplayer.com/todos`, enter PIN. Should return JSON like:

```json
{"notBefore":"...","notAfter":"...","firstNames":"...","lastName":"...","nationalNumber":"...","country":"BE"}
```

In **both** Chrome and Firefox.
