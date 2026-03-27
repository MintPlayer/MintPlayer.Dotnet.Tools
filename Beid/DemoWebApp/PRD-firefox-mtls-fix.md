# PRD: Fix Firefox Hanging on mTLS Client Certificate Authentication

**Status:** Draft
**Date:** 2026-03-27
**App:** Belgian eID DemoWebApp (`eid.mintplayer.com`)

---

## Problem Statement

The Belgian eID DemoWebApp at `https://eid.mintplayer.com/todos` works correctly in Chrome and other Chromium-based browsers but **hangs indefinitely** in Firefox. Eventually Firefox shows a connection error ("Firefox kan geen verbinding maken met de server op eid.mintplayer.com"). No certificate prompt appears, no error page is shown -- the browser just hangs until timeout.

The app uses mTLS (mutual TLS) with client certificates from Belgian eID smart cards, deployed behind Traefik as a reverse proxy.

---

## Root Cause Analysis

### Primary Cause: Missing CA Names in TLS CertificateRequest

The Traefik TLS config uses `clientAuthType: RequestClientCert` **without specifying `caFiles`**. This means the `CertificateRequest` TLS message sent during the handshake contains an **empty CA list**.

- **Chrome** handles an empty CA list gracefully -- it still prompts the user to select from all available client certificates in the OS store.
- **Firefox** uses the CA list to determine which certificates to offer. With an empty list, Firefox has no hint about which certificate the server expects. Instead of prompting, Firefox either sends no certificate or hangs during the handshake.

**Evidence:** The Belgian government's eID authentication server (`ccff02.minfin.fgov.be`, used by taxonweb.be) works in Firefox because it sends specific Belgian CA names (Citizen CA, Root CA, etc.) in the `CertificateRequest`, telling Firefox exactly which certificates to present. This was confirmed by the user -- the same eID card + Firefox combination works on taxonweb.be.

**Supporting reference:** [traefik#10643 comment](https://github.com/traefik/traefik/issues/10643#issuecomment-2106752593) shows a working Traefik mTLS config that includes `caFiles` pointing to the signing CA certificate.

### Secondary Concern: HTTP/2 + TLS Renegotiation

Traefik enables HTTP/2 by default. While `RequestClientCert` should request the certificate during the initial TLS handshake (before HTTP/2 ALPN negotiation), there are known Traefik edge cases ([traefik#10643](https://github.com/traefik/traefik/issues/10643), [traefik#10134](https://github.com/traefik/traefik/issues/10134)) where this may not work reliably. HTTP/2 strictly forbids TLS renegotiation (RFC 7540 Section 9.2.1). If the `caFiles` fix alone doesn't resolve Firefox, disabling HTTP/2 via `alpnProtocols: [http/1.1]` is the fallback.

---

## Current Architecture

```
Browser (eID smart card)
  | TLS 1.2 + HTTP/2 (ALPN)
  v
Traefik (port 443, websecure)
  - tls.options: mtls@file (TLS 1.2 only, RequestClientCert)
  - middleware: passTLSClientCert (pem=true)
  - certresolver: letsencrypt
  |
  | X-Forwarded-Tls-Client-Cert header (URL-encoded base64 DER)
  v
.NET app (port 8080, HTTP)
  - CertificateForwarding middleware
  - Reads cert from header
  - Extracts PersonInfo via BouncyCastle
```

**Key config files:**
- `traefik-dynamic-config/tls.yml` -- TLS options (TLS 1.2, RequestClientCert)
- `docker-compose.yml` -- Traefik labels (router, middleware)
- `Program.cs` -- Certificate forwarding + endpoint logic

---

## Proposed Solution

### Phase 1: Server-Side Fix (Traefik Config) -- High Priority

#### 1A. Disable HTTP/2 for the mTLS Router

Force HTTP/1.1 on the `eid` router to avoid the HTTP/2 renegotiation deadlock. Traefik does not natively support per-router HTTP/2 disable, so there are two approaches:

**Option A -- Dedicated entrypoint (Recommended):**

Create a separate HTTPS entrypoint with HTTP/2 disabled specifically for mTLS:

```yaml
# traefik static config (traefik.yml)
entryPoints:
  websecure-mtls:
    address: ":8443"   # or use :443 if websecure can be moved
    http2:
      maxConcurrentStreams: 0  # Effectively disables HTTP/2
    # OR use Traefik v3 syntax:
    # protocols:
    #   http: ["h1"]
```

Then update the docker-compose label:
```yaml
- "traefik.http.routers.eid.entrypoints=websecure-mtls"
```

**Option B -- ALPN restriction in TLS options:**

In `tls.yml`, restrict ALPN to HTTP/1.1 only:

```yaml
tls:
  options:
    mtls:
      minVersion: VersionTLS12
      maxVersion: VersionTLS12
      alpnProtocols:
        - http/1.1
      clientAuth:
        clientAuthType: RequestClientCert
```

This prevents HTTP/2 negotiation on connections using the `mtls` TLS option, which is the simplest change.

#### 1B. Add Belgian Root CA to Traefik TLS Config (Optional Enhancement)

Adding the Belgian Root CA to the `clientAuth.caFiles` gives Firefox a hint about which certificates to present:

```yaml
tls:
  options:
    mtls:
      minVersion: VersionTLS12
      maxVersion: VersionTLS12
      alpnProtocols:
        - http/1.1
      clientAuth:
        clientAuthType: RequestClientCert
        caFiles:
          - /etc/traefik/certs/belgium-root-ca4.pem
```

When the server includes CA names in the CertificateRequest, Firefox can filter its certificates and prompt the user with only matching ones. Without this, Firefox may not know which certificate to present.

**Deployment:** Mount the `BelgiumRoot.cer` (already in the repo) into the Traefik container.

### Phase 2: Documentation / User Guidance -- Medium Priority

#### 2A. Firefox PKCS#11 Setup Guide

Create user-facing documentation explaining how to load the Belgian eID PKCS#11 module into Firefox:

1. Open Firefox Settings > Privacy & Security > Security Devices
2. Click "Load"
3. Module Name: `Belgium eID`
4. Module filename:
   - Windows: `C:\Windows\System32\beidpkcs11.dll`
   - macOS: `/usr/local/lib/beid-pkcs11.bundle/Contents/MacOS/libbeidpkcs11.dylib`
   - Linux: `/usr/lib/x86_64-linux-gnu/libbeidpkcs11.so`
5. Restart Firefox

#### 2B. Firefox about:config Recommendations

Document recommended Firefox settings:
- `security.tls.enable_post_handshake_auth` = `true` (for future TLS 1.3 support)
- `security.osclientcerts.autoload` = `true` (loads OS client certs, Firefox 75+)

### Phase 3: Graceful Degradation -- Low Priority

#### 3A. Connection Timeout Guidance

Add a user-facing message on the app (e.g., on the root `/` page) that detects Firefox and provides setup instructions before the user navigates to `/todos`.

#### 3B. Health Check Fix

Fix the typo in the health check endpoint: `"/healtz"` should be `"/healthz"` (line 131 of Program.cs).

---

## Implementation Plan

| # | Task | Files Changed | Priority | Effort |
|---|------|---------------|----------|--------|
| 1 | Add `alpnProtocols: [http/1.1]` to Traefik TLS options | `traefik-dynamic-config/tls.yml` | P0 | Small |
| 2 | Add Belgian Root CA to `clientAuth.caFiles` | `tls.yml`, `docker-compose.yml`, deploy workflow | P1 | Medium |
| 3 | Create Firefox setup documentation page | New doc or in-app page | P2 | Medium |
| 4 | Fix healthz typo | `Program.cs:131` | P3 | Trivial |

---

## Testing Plan

### Server-side validation
- [ ] Deploy updated Traefik TLS config to staging/VPS
- [ ] Verify `curl -v https://eid.mintplayer.com/` shows HTTP/1.1 (not h2) in protocol
- [ ] Verify Chrome still works (eID card present, `/todos` returns PersonInfo)
- [ ] Verify Firefox with eID PKCS#11 module loaded -- should prompt for certificate and display PersonInfo
- [ ] Verify Firefox without eID card -- should get 401 response (not hang)
- [ ] Verify no regression on the Let's Encrypt certificate (still valid, auto-renews)

### Browser matrix
| Browser | eID Card | Expected Result |
|---------|----------|-----------------|
| Chrome (Win) | Present | Certificate prompt, PersonInfo returned |
| Chrome (Win) | Absent | 401 error (no hang) |
| Firefox (Win) | Present + PKCS#11 loaded | Certificate prompt, PersonInfo returned |
| Firefox (Win) | Present + PKCS#11 NOT loaded | 401 error or "no cert" message (NOT hang) |
| Firefox (Win) | Absent | 401 error (NOT hang) |
| Edge (Win) | Present | Same as Chrome |

### Regression checks
- [ ] `/` returns "Hello" (no auth required)
- [ ] `/todos/{id}` returns todo item
- [ ] `/healthz` returns 200
- [ ] Docker image builds and deploys via CI/CD pipeline

---

## References

- [RFC 7540 Section 9.2.1](https://www.rfc-editor.org/rfc/rfc7540#section-9.2.1) -- HTTP/2 forbids TLS renegotiation
- [RFC 8740](https://www.rfc-editor.org/rfc/rfc8740) -- TLS 1.3 with HTTP/2 forbids PHA
- [Traefik #10643](https://github.com/traefik/traefik/issues/10643) -- mTLS browser cert prompt issues
- [Traefik #10134](https://github.com/traefik/traefik/issues/10134) -- RequireAndVerifyClientCert doesn't actively request cert
- [Traefik #12183](https://github.com/traefik/traefik/issues/12183) -- passTLSClientCert forwards intermediate instead of leaf
- [Firefox Bug #1511989](https://bugzilla.mozilla.org/show_bug.cgi?id=1511989) -- PHA disabled by default
- [Firefox Bug #1662607](https://bugzilla.mozilla.org/show_bug.cgi?id=1662607) -- No client cert prompt with mTLS
- [Go #40521](https://github.com/golang/go/issues/40521) -- Go doesn't support TLS 1.3 PHA

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Disabling HTTP/2 impacts performance | Low | Low | mTLS endpoints serve small JSON payloads; HTTP/2 multiplexing provides negligible benefit |
| ALPN restriction not supported in Traefik version | Low | Medium | Fall back to dedicated entrypoint approach (Option A) |
| Belgian Root CA file needs updating when rotated | Low | Low | Belgium CA certs have long validity; monitor expiry |
| Users don't load PKCS#11 module in Firefox | Medium | Medium | Provide clear documentation and in-app guidance |
