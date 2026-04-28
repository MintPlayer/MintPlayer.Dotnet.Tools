# Firefox + Belgian eID mTLS Hang: Root Cause Analysis

**Date:** 2026-04-28
**App:** Belgian eID DemoWebApp (`eid.mintplayer.com`, also `https://localhost:5050` locally)
**Status:** Diagnosed; fix not yet implemented

---

## Symptom

Connecting to the app with a Belgian eID card:

- **Chrome (Windows):** works. PIN prompt → enter PIN → `/todos` returns the expected JSON.
- **Firefox (Windows):** PIN prompt appears, user enters PIN, page hangs ~2 minutes, then "De wachttijd voor de verbinding is verstreken" (connection timeout). No certificate error, no TLS alert displayed to the user.

The hang reproduces against **both** the VPS deployment (Traefik / Go TLS) **and** the local development server (Kestrel / Schannel). It has never worked in Firefox.

A previous PRD (`PRD-firefox-mtls-fix.md`) addressed an earlier-stage symptom (no PIN prompt at all) by adding `caFiles` and ALPN restrictions. Those fixes did get the PIN prompt to appear — but the hang after PIN entry is a separate problem with a different root cause.

---

## Root Cause: RSA-PSS in `signature_algorithms`

Traefik (and Kestrel server-mode on Windows) advertises an mTLS `CertificateRequest` whose `signature_algorithms` extension lists **`rsa_pss_rsae_sha256` first**, with `rsa_pkcs1_sha256` only sixth:

```
signature_algorithms (len=20)
  rsa_pss_rsae_sha256 (0x0804)   ← FIRST
  ecdsa_secp256r1_sha256 (0x0403)
  ed25519 (0x0807)
  rsa_pss_rsae_sha384
  rsa_pss_rsae_sha512
  rsa_pkcs1_sha256 (0x0401)       ← only 6th
  rsa_pkcs1_sha384
  rsa_pkcs1_sha512
  ecdsa_secp384r1_sha384
  ecdsa_secp521r1_sha512
```

Captured live with `openssl s_client -connect eid.mintplayer.com:443 -tls1_2 -trace`.

### Failure sequence in Firefox

1. NSS picks the **first** scheme in the server's list whose key algorithm matches the chosen client cert. The eID auth cert is RSA-2048 → first match is `rsa_pss_rsae_sha256` (PSS).
2. NSS calls `C_Sign` on the eID PKCS#11 module (`beidpkcs11.dll`) requesting an RSA-PSS signature.
3. Belgian eID cards (especially the 2017-era card under test) **do not implement RSA-PSS signing** in their applet — they only support `RSASSA-PKCS1-v1_5`. The card returns `CKR_MECHANISM_INVALID`, or the middleware hangs the call.
4. NSS does **not** fall back to a different signature scheme. The `CertificateVerify` is never produced. The TLS handshake stalls until the TCP connection times out.

PSS support was only added on the card-applet side starting roughly with eID v1.9 / middleware 5.x in 2024. Any card issued before that — including this 2017 card — is PKCS#1 v1.5 only.

### Why Chrome works

Chrome on Windows uses **Schannel → MSCAPI → eID CSP**, not PKCS#11. The CSP advertises to MSCAPI only the algorithms the card actually supports (`RSASSA-PKCS1-v1_5`), so Schannel pre-filters PSS out of the negotiation and signs with PKCS#1 v1.5. Different code path entirely; the bug doesn't apply.

### Why `taxonweb.be` works in Firefox with the same card

Belgian government portals (CSAM, Itsme, etc.) use **federated SAML / OIDC**, not client-cert TLS. The eID is used after the TLS handshake completes — there is no `CertificateVerify` step on the card key during the handshake, so the PSS-vs-PKCS#1-v1_5 question never arises.

### Why Kestrel local server fails the same way

`Program.cs:86-103` configures Kestrel for development with `ClientCertificateMode.RequireCertificate`. The TLS handshake is performed by Schannel in server mode, which on modern Windows also advertises RSA-PSS first in `signature_algorithms`. Same root cause, different layer. (Kestrel additionally has no per-app `caFiles` equivalent, but that's a separate, smaller issue.)

---

## Why this can't be fixed in Traefik (or Caddy)

Go's `crypto/tls` **hardcodes** the advertised list in `defaultSupportedSignatureAlgorithms` (in `crypto/tls/common.go`). `tls.Config` exposes no `SignatureAlgorithms` field. Traefik can't surface a knob that Go doesn't expose. Same applies to Caddy v2 (also Go).

Tracking issue: [golang/go#45266](https://github.com/golang/go/issues/45266) — open, no movement.

The `caFiles` and `alpnProtocols` fixes already in `traefik-dynamic-config/tls.yml` are still correct and necessary; they're just not sufficient.

---

## Fix Options

### Option A — nginx as a TLS terminator for the `eid` router (recommended)

Keep Traefik for everything else on the VPS. Add an `nginx-eid` container that handles **only** `eid.mintplayer.com`:

- nginx (OpenSSL-backed) supports `ssl_conf_command SignatureAlgorithms RSA+SHA256:RSA+SHA384:RSA+SHA512;` which advertises **only** PKCS#1 v1.5 in the `CertificateRequest`. PSS is never offered, NSS never tries it.
- nginx forwards the client cert to the .NET app exactly like Traefik does today (header containing PEM/DER), so `Program.cs` doesn't change.
- Traefik can still handle the Let's Encrypt cert for the public TLS, with nginx terminating internally — or nginx can own its own ACME via certbot. Either layout works.

Trade-off: one more container, slightly more deploy complexity.

### Option B — HAProxy terminator

Same idea as A. HAProxy also uses OpenSSL and supports `ssl-default-bind-options` + `SignatureAlgorithms`. Equivalent outcome; pick whichever the team is more comfortable operating.

### Option C — "Chrome only" with a Firefox banner

Detect Firefox on `/` and render a notice explaining the limitation. Smallest engineering cost; punts the problem to users. Reasonable if this app stays a demo.

### Option D — Wait on Go

Not a real option. The Go issue has been open since 2021 with no implementation progress.

---

## Recommendation

Go with **Option A**. It is the only fix that makes Firefox work without changing per-user OS or browser configuration, and it isolates the workaround to one container that's clearly labeled as "Firefox-compatibility shim for Belgian eID mTLS." If/when Go fixes its `crypto/tls` API, the shim can be removed and Traefik can take the route back over.

---

## What's already in place (for context)

- `traefik-dynamic-config/tls.yml`: `clientAuthType: RequestClientCert`, TLS 1.2 only, `alpnProtocols: [http/1.1]`, `caFiles: [belgium-root-ca4.pem, citizen-ca.pem]`. All correct, leave as-is.
- `caFiles` matches the user's card issuer (`Citizen CA serialNumber=201701`, SHA1 `D1:2D:09:75:07:15:BC:5A:37:57:EA:62:04:5A:98:6D:BC:5A:F1:E9`) — verified by exporting the intermediate from the eID Viewer and comparing fingerprints.
- Live `openssl s_client` confirms the server presents both CA names correctly and negotiates ALPN as `http/1.1`.
- `passTLSClientCert` middleware + `CertificateForwarding` in `Program.cs` is correct and would be reused unchanged behind nginx.

---

## References

- [golang/go#45266](https://github.com/golang/go/issues/45266) — proposal to expose `SignatureAlgorithms` on `tls.Config` (open, stalled)
- [Mozilla Bug 1662607](https://bugzilla.mozilla.org/show_bug.cgi?id=1662607) — NSS doesn't fall back to alternate sig algs on client-cert signing failure
- nginx [`ssl_conf_command`](https://nginx.org/en/docs/http/ngx_http_ssl_module.html#ssl_conf_command) — passthrough to OpenSSL config commands including `SignatureAlgorithms`
- RFC 5246 §7.4.1.4.1 — TLS 1.2 `signature_algorithms` extension (server side controls the offered list)
- Belgian eID middleware release notes — PSS support timeline
