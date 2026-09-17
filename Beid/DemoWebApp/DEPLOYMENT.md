# eID DemoWebApp — Server Deployment Map

**Authoritative inventory of what must exist on the VPS, who puts it there, and what breaks if it's missing.**

`Readme.md` explains *why* the architecture looks like this. This file answers *what has to be where*. Paths below are the real ones; the host itself is written as `<vps-host>` and secrets are never shown.

Verified against the live server on **2026-09-17**.

---

## 1. The picture

```
                        Internet :80 / :443
                               │
                               ▼
                  ┌────────────────────────────┐
                  │  traefik  (the ONLY container that binds host ports)
                  │  /var/www/traefik/         │
                  │                            │
                  │  :80  → redirect to :443   │
                  │  :443 ─┬─ other SNIs → terminates TLS → their containers
                  │        │                   │
                  │        └─ SNI eid.mintplayer.com
                  │           TCP PASSTHROUGH — no TLS termination,
                  │           no CertificateRequest, no plaintext
                  └────────────┬───────────────┘
                               │ encrypted bytes, over the `web` docker network
                               ▼
                  ┌────────────────────────────┐
                  │  nginx-eid                 │
                  │  /var/www/eid/             │
                  │                            │
                  │  terminates TLS 1.2        │
                  │  requests the client cert  │
                  │  advertises NO RSA-PSS ←── the entire reason this container exists
                  └────────────┬───────────────┘
                               │ plain HTTP + X-Forwarded-Tls-Client-Cert
                               ▼
                  ┌────────────────────────────┐
                  │  eid  (.NET, :8080)        │
                  └────────────────────────────┘
```

Two containers make up this app (`nginx-eid`, `eid`), both defined in `/var/www/eid/docker-compose.yml`. Neither publishes a host port; all traffic arrives through Traefik over the shared `web` network.

---

## 2. What must exist on the server

### `/var/www/eid/` — this app. Fully managed by CI.

Every file here is overwritten on each deploy by `.github/workflows/eid-deploy.yml`. **Do not hand-edit** — changes are silently lost on the next push to `master`.

| Path | Source in repo | Purpose | If missing |
|---|---|---|---|
| `docker-compose.yml` | `Beid/DemoWebApp/docker-compose.yml` | Defines `nginx-eid` + `eid`, the Traefik passthrough labels, and all mounts | Nothing deploys |
| `nginx.conf` | `Beid/DemoWebApp/nginx.conf` | TLS 1.2, mTLS, the no-PSS `signature_algorithms` override, proxy to `eid:8080` | nginx won't start |
| `traefik-dynamic-config/belgian-eid-cas.pem` | same path in repo | The four Belgian CA names nginx advertises in the `CertificateRequest` | **nginx won't start.** Even if it did, no browser would offer the eID card — no PIN prompt, ever |

> **The folder name `traefik-dynamic-config/` is historical and misleading.** It dates from when Traefik terminated TLS for this host. It now contains *only* nginx's client-CA material. **It is live and load-bearing — do not delete it.** It is not the same thing as `/var/www/traefik/dynamic/` (§5).

### `/var/www/traefik/` — shared infrastructure. NOT managed by this repo.

Hand-maintained, shared by every site on the box. This app depends on two things here.

| Path | Purpose | If missing |
|---|---|---|
| `docker-compose.yml` → `traefik` service | Binds `:80`/`:443`, routes by SNI, runs the ACME loop | Every site on the VPS goes down |
| `docker-compose.yml` → `certs-dumper` service | Watches `acme.json`, writes plain PEMs nginx can mount | Cert silently stops refreshing; eid breaks ~90 days later at expiry |
| `dumped-certs/eid.mintplayer.com/certificate.pem` | nginx's server cert | nginx won't start |
| `dumped-certs/eid.mintplayer.com/privatekey.pem` | its key (mode `0600`) | nginx won't start |

The `certs-dumper` service must carry these flags, or it writes different filenames than `nginx.conf` expects:

```yaml
  certs-dumper:
    image: ldez/traefik-certs-dumper:latest
    command:
      - file
      - --version=v3
      - --watch
      - --source=/letsencrypt/acme.json
      - --dest=/dumped
      - --domain-subdir=true      # creates the per-domain subdirectory
      - --crt-name=certificate    # ─┐ these four must match the
      - --key-name=privatekey     #  │ ssl_certificate / ssl_certificate_key
      - --crt-ext=.pem            #  │ paths in nginx.conf
      - --key-ext=.pem            # ─┘
    volumes:
      - letsencrypt:/letsencrypt:ro
      - /var/www/traefik/dumped-certs:/dumped
    restart: unless-stopped
```

### Host-level state (outside Docker, outside this repo)

| What | Value | Why |
|---|---|---|
| Docker network | `web`, external, pre-existing | How Traefik reaches both containers. `docker network create web` if absent |
| Root crontab | `0 4 * * * docker exec nginx-eid nginx -s reload >/dev/null 2>&1` | nginx caches its cert at startup. Without this it keeps serving the **expired** cert after renewal until something restarts it |
| DNS | `eid.mintplayer.com` A record → VPS | ACME HTTP-01 and normal routing |
| Container name pin | `container_name: nginx-eid` in `docker-compose.yml` | The cron line targets this exact name. Without the pin Compose would call it `eid-nginx-eid-1` and the reload would silently no-op |

---

## 3. How a deploy works

`.github/workflows/eid-deploy.yml`, on push to `master` touching `Beid/DemoWebApp/**`:

1. Builds `Beid/DemoWebApp/Dockerfile`, pushes to `ghcr.io/mintplayer/mintplayer-dotnet-tools-eid:master`.
2. SSHes to the VPS and, in `/var/www/eid/`:
   - `curl`s `docker-compose.yml`, `nginx.conf` and `traefik-dynamic-config/belgian-eid-cas.pem` from `raw.githubusercontent.com` at `master`
   - `docker compose pull && down && up -d`, then `docker image prune -f`

Consequences worth knowing:

- **Those three files are the only ones deployed.** Everything else in `Beid/DemoWebApp/` (docs, the individual CA PEMs, the .NET sources) never lands on the server — the app code arrives baked into the container image.
- **The files are fetched from `master`, not from the commit that triggered the run.** A push landing mid-deploy can mix versions.
- **It is a `down`/`up`, not a rolling restart** — a few seconds of downtime per deploy.
- The cert directory `/var/www/traefik/dumped-certs` is `mkdir -p`'d but never populated by this workflow; that's `certs-dumper`'s job.

---

## 4. Certificate lifecycle

```
Traefik ACME loop ──► /letsencrypt/acme.json (docker volume, Traefik-private)
                              │
                       certs-dumper --watch
                              ▼
        /var/www/traefik/dumped-certs/eid.mintplayer.com/{certificate,privatekey}.pem
                              │  (bind-mounted read-only into nginx-eid)
                              ▼
                     nginx :443  ──► reloaded nightly at 04:00 by cron
```

Let's Encrypt certs last 90 days; Traefik renews ~30 days before expiry. The dumper writes the new files within seconds, but **nginx only re-reads its certificate on reload** — hence the cron. A daily reload for a ~60-day event is deliberate overkill and harmless.

> **There is intentionally NO Traefik HTTP router for `eid.mintplayer.com`.** Adding one with `tls.certresolver` on the `websecure` entrypoint collides with the TCP passthrough router: Traefik terminates TLS itself and forwards plain HTTP to nginx, which answers `400 Bad Request — The plain HTTP request was sent to HTTPS port`. The cert is still renewed without a router, because Traefik's ACME loop is router-independent once the domain is in `acme.json`. For a **fresh** server where `acme.json` has no entry yet, see `Readme.md` → "First-time cert acquisition".

---

## 5. Stale files that can be deleted

`/var/www/traefik/dynamic/` holds leftovers from the pre-2026-04-28 design, when Traefik terminated TLS for this host:

```
/var/www/traefik/dynamic/tls.yml               # the `mtls` TLS option — referenced by nothing
/var/www/traefik/dynamic/belgium-root-ca4.pem  # only existed for tls.yml's caFiles
/var/www/traefik/dynamic/citizen-ca.pem        # ditto
/var/www/traefik/dynamic/certs/                # three more orphaned PEMs
```

Verified inert on 2026-09-17: no container label and no compose file on the server references `tls.options` or `mtls`, and a TCP passthrough router cannot accept a `tls.options` reference at all. The option is named `mtls`, **not** `default`, so it is not quietly applying to other sites either.

**Delete the files, keep the directory** — Traefik runs with `--providers.file.directory=/etc/traefik/dynamic --providers.file.watch=true`. An empty directory is fine (all routing comes from the Docker provider); a missing mount source just makes it noisy.

Again: this is **not** `/var/www/eid/traefik-dynamic-config/`, which is live.

---

## 6. Verifying a deployment

```bash
# 1. The check that matters — the advertised schemes must contain NO rsa_pss_*.
echo | openssl s_client -connect eid.mintplayer.com:443 -servername eid.mintplayer.com \
    -tls1_2 -trace 2>&1 | grep -E "Requested Signature Algorithms|Peer signature type"
```

Expected (captured 2026-09-17):

```
Requested Signature Algorithms: RSA+SHA256:RSA+SHA384:RSA+SHA512:ECDSA+SHA256:ECDSA+SHA384:ECDSA+SHA512
Peer signature type: rsa_pkcs1_sha256
```

Six schemes, zero RSA-PSS. If you instead see a ten-entry list starting with `rsa_pss_rsae_sha256`, **Traefik has taken TLS termination back over** and Firefox will hang after PIN entry — see `FIREFOX-MTLS-PSS-ROOTCAUSE.md`.

```bash
# 2. Endpoint sanity, no card present — expect 401 from a valid TLS connection.
curl -s -o /dev/null -w "http=%{http_code}\n" https://eid.mintplayer.com/todos

# 3. On the server: both containers up, cert not near expiry.
docker ps --filter name=nginx-eid --filter name=eid --format "{{.Names}}\t{{.Status}}"
openssl x509 -in /var/www/traefik/dumped-certs/eid.mintplayer.com/certificate.pem \
    -noout -subject -enddate -issuer
```

Full end-to-end test: insert an eID card, open `https://eid.mintplayer.com/todos`, enter the PIN. Expect the cardholder's `PersonInfo` as JSON, **in both Chrome and Firefox** — Firefox is the one that regresses if the TLS layer changes.

---

## 7. Failure modes

| Symptom | Most likely cause |
|---|---|
| `400 Bad Request — plain HTTP request was sent to HTTPS port` | A Traefik HTTP router exists for this host and is terminating TLS ahead of the passthrough router (§4) |
| Firefox hangs ~2 min after PIN entry, Chrome fine | RSA-PSS is being advertised — either Traefik is terminating, or `ssl_conf_command` was dropped from `nginx.conf` |
| No PIN prompt at all | `belgian-eid-cas.pem` missing, unmounted, or lacking the card's issuing CA — the browser never learns the card is relevant |
| `nginx-eid` crash-loops on start | A mounted file is absent: the cert pair under `dumped-certs/`, or the CA bundle |
| Site fine for ~60 days, then expired-cert errors | `certs-dumper` stopped, or the 04:00 reload cron is gone / the container was renamed |
| Newer ECC eID card rejected while older RSA cards work | The CA6 / Citizen CA 202002 pair is missing from the bundle |

---

## 8. Rebuilding from scratch

1. `docker network create web` (if it doesn't exist).
2. Point DNS for `eid.mintplayer.com` at the host.
3. Bring up Traefik from `/var/www/traefik/docker-compose.yml`, including the `certs-dumper` service from §2.
4. Obtain the cert — `Readme.md` → "First-time cert acquisition" (temporary HTTP router on the `web` entrypoint, **port 80, not `websecure`**, then remove it).
5. Confirm `/var/www/traefik/dumped-certs/eid.mintplayer.com/` contains `certificate.pem` + `privatekey.pem`.
6. Install the reload cron from §2.
7. Run the `eid-deploy.yml` workflow (`workflow_dispatch`) to populate `/var/www/eid/` and start both containers.
8. Verify with §6.

Steps 1–6 are one-time host setup. Step 7 repeats on every push.

---

## Related documents

| Document | Covers |
|---|---|
| `Readme.md` | Architecture and the reasoning behind it; local development |
| `FIREFOX-MTLS-PSS-ROOTCAUSE.md` | Why a non-Go TLS terminator is required, with `openssl -trace` evidence |
| `CLIENT-CERT-FORWARDING.md` | The `X-Forwarded-Tls-Client-Cert` header flow and the trust model |
| `PRD-tls-version-independence.md` | Why TLS 1.2 is pinned |
| `Create-Certificate.md` | Local-dev certificate generation |
