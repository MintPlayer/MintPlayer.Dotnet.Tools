# PRD + Plan: Making Belgian eID Auth TLS-Version-Independent

**Status:** Draft / decision-pending
**Date:** 2026-07-06
**App:** Belgian eID DemoWebApp (`eid.mintplayer.com`, `https://localhost:5050` locally)
**Supersedes for this topic:** nothing — complements [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md)

---

## 1. Question this document answers

> The app works, but only because TLS is pinned to **exactly TLS 1.2**
> (`Program.cs:93` for Kestrel dev, `nginx.conf:10` for prod). TLS 1.2 won't
> be around forever. **Why does eID only work on TLS 1.2, and is there a way
> to make it work on any TLS version?**

Short answer: **for the current mTLS design, TLS 1.2 is not a tunable — it is the only TLS version in which a Belgian eID card can authenticate at all.** No proxy or server setting can change that. Becoming TLS-version-independent requires a different architecture: **take the card key out of the TLS handshake.** That is achievable and is specified below.

---

## 2. Root cause — why TLS 1.2 is mandatory (not optional)

Three facts combine into a hard protocol wall.

### 2.1 The eID card signs with RSA PKCS#1 v1.5

The card's signing capability is gated by **applet version**, read from the card and mapped in `CCard::GetSupportedAlgorithms()` (`eid-mw/cardcomm/pkcs11/src/cardlayer/card.cpp:870-887`):

| Applet version | RSA PKCS#1 v1.5 | RSA-PSS | ECDSA |
|---|---|---|---|
| **< 1.7** | ✅ (MD5/SHA1 + SHA256) | ❌ | ❌ |
| **== 1.7** | ✅ | ⚠️ SHA1-PSS + SHA256-PSS, **combined mechanisms only** (`CKM_SHA256_RSA_PKCS_PSS`, not raw `CKM_RSA_PKCS_PSS`) | ❌ |
| **≥ 1.8** (newer ECC cards) | ❌ | ❌ | ✅ only |

The 2017-era card in use is almost certainly **applet < 1.7 → PKCS#1 v1.5 only**. The middleware never constructs PSS padding itself; it sends a padding-selector byte in the `MSE:SET` APDU, and the applet refuses PSS unless it is exactly version 1.7 (`ALLOWED_APPLET17_ONLY` guard, `card.cpp:895,926-933`).

### 2.2 TLS 1.3 forbids PKCS#1 v1.5 for handshake signatures

RFC 8446 §4.2.3, verbatim: the `rsa_pkcs1_*` codepoints

> "…are not defined for use in signed TLS handshake messages, although they MAY appear in `signature_algorithms`… for backward compatibility with TLS 1.2."

`CertificateVerify` **is** a signed handshake message. So in TLS 1.3 an RSA client cert **must** sign it with `rsa_pss_rsae_*`. RSASSA-PSS support is mandatory in a TLS 1.3 stack, and there is **no in-protocol fallback** — if the token can't do PSS, the handshake dies. NSS (Firefox) then sends an empty Certificate message and auth silently fails (Mozilla bug 1588941); CVE-2019-11727 removed NSS's earlier, non-compliant attempt to sign 1.3 `CertificateVerify` with PKCS#1 v1.5.

### 2.3 TLS 1.2 is the only version where PKCS#1 v1.5 CertificateVerify is legal

Under RFC 5246, RSA `CertificateVerify` **is** PKCS#1 v1.5, and the **server** dictates the offered schemes via the `CertificateRequest.supported_signature_algorithms` list. That is exactly the knob `nginx.conf:18` uses:

```
ssl_conf_command SignatureAlgorithms RSA+SHA256:RSA+SHA384:RSA+SHA512;
```

It forces the server to advertise **only** PKCS#1 v1.5, so Firefox/NSS never even tries PSS. Go's `crypto/tls` (Traefik) can't do this — it hardcodes PSS first and exposes no knob ([golang/go#45266](https://github.com/golang/go/issues/45266)) — which is the whole reason the nginx shim exists.

### 2.4 Conclusion

```
PKCS#1-v1.5-only card  +  TLS 1.3 bans PKCS#1-v1.5 handshake sigs
        ⇒  eID mTLS is IMPOSSIBLE on TLS 1.3, by protocol.
        ⇒  TLS 1.2 is the ONLY option, forever, for this design.
```

Pinning TLS 1.2 is **correct**, not a workaround to be removed. There is nothing to "fix" at the TLS layer.

---

## 3. Two second-order problems with the status quo

1. **New ECC cards were excluded (now fixed within TLS 1.2 — see §3.2).** The old `SignatureAlgorithms RSA+…` line advertised only PKCS#1 v1.5, so an applet-≥1.8 card — which does **ECDSA only** — could not authenticate at all. The setup was silently pinned to old RSA cards.
2. **TLS 1.2 end-of-life is a hard cliff.** Whenever browsers eventually drop TLS 1.2 (years out, but real), eID mTLS with these cards breaks with **no server-side remedy**. There is no gradual migration path within the mTLS design.

Problem 2 is only cured by §4 (stop using the card key as the TLS handshake key). Problem 1 is curable *inside* the mTLS design and has been addressed — see below.

### 3.1 Does a TLS 1.2 end-of-life force Belgium to re-issue every old card?

**No.** The card is not tied to TLS 1.2 — only the **mTLS relying-party pattern** is, and that pattern is a choice made by *this app*, not by the card or the government.

- **PKCS#1 v1.5 the algorithm is neither broken nor deprecated.** TLS 1.3 only bans it *inside the handshake*. The same card signs PKCS#1 v1.5 fine at the **application layer**, where no TLS version has an opinion.
- **This is exactly why the Belgian government doesn't use mTLS.** taxonweb/CSAM/itsme use federated auth — the card is used *after* TLS is established — so a TLS 1.2 EOL is a non-event for them, on any browser and any future TLS version.
- If TLS 1.2 were dropped, the response is **relying parties migrate their apps off TLS-layer client auth** (the §4 migration), not a mass card re-issue. The government already did this years ago.
- Incidentally, the fleet is *already* moving RSA → ECC via normal 10-year renewal, and ECC (ECDSA) **is** legal in TLS 1.3 handshakes — so renewed ECC cards would even restore TLS 1.3 mTLS. That's natural attrition, not a rescue plan, and does nothing for today's RSA cards. (Even a PSS-capable 1.7 RSA card likely still fails TLS 1.3 mTLS: NSS/Schannel drive the *raw* `CKM_RSA_PKCS_PSS` mechanism the applet doesn't expose.)

### 3.2 Can we keep TLS 1.2 pinned AND support both RSA and ECC cards? (Yes — applied)

Keeping TLS 1.2 pinned, we broaden the `CertificateRequest` `signature_algorithms` to advertise both PKCS#1 v1.5 (RSA cards) **and** ECDSA (ECC cards), while still **never** offering RSA-PSS (which would re-break Firefox for RSA cards):

```nginx
# nginx.conf — was: RSA+SHA256:RSA+SHA384:RSA+SHA512
ssl_conf_command SignatureAlgorithms RSA+SHA256:RSA+SHA384:RSA+SHA512:ECDSA+SHA256:ECDSA+SHA384:ECDSA+SHA512;
```

The browser selects the scheme matching the *selected card's key type*: RSA cards sign `RSA+SHA256` (v1.5, unchanged), ECC cards sign `ECDSA+SHA256`. No regression for RSA; ECC cards now work. All still under pinned TLS 1.2. **No .NET change** — `PersonInfo` parses the subject DN via BouncyCastle and the cert is forwarded as PEM, both key-algorithm-agnostic. **No Dockerfile change** — nginx runs the stock `nginx:alpine` image with `nginx.conf` and the CA bundle mounted as compose volumes.

**Remaining caveat — the CA bundle.** `ssl_client_certificate` (the acceptable-CA names in the `CertificateRequest`) currently lists only `Belgium Root CA4` + `Citizen CA 201701`. Firefox only *offers* a card whose issuer is in that list, so ECC cards won't appear until their issuing Citizen CA (and root, if newer) is added to `traefik-dynamic-config/belgian-eid-cas.pem`. This is a cert-collection task, tracked in §6 Track 1.

---

## 4. The only real solution — decouple eID from the TLS handshake

If the eID key is not used in `CertificateVerify`, the PSS-vs-v1.5 question never arises and **any TLS version works** (this is exactly why itsme / CSAM / taxonweb work in every browser on any TLS version — the card is used *after* TLS completes). Two viable architectures:

### Option A — Application-layer eID (fits this codebase)

Use `MintPlayer.EidReader` (already in this repo: PC/SC `SELECT FILE` + `READ BINARY`, `Card.cs`) to read the card **outside** the TLS layer, then authenticate at the application layer. Ordinary server TLS (any version, PSS-only, HTTP/2 — all fine) carries the result.

Two strengths of authentication are possible:

- **A1 — Identity read + RRN-signature verification (no live card signing).** Read the `Id`/`Address` files plus their National-Register signature files (`IdSig`, `AddressSig` — already selectable in `EEidFile`) and the `RrnCert` chain. Verifying those signatures proves the data is authentic and unmodified. *Does not* prove card possession/PIN, so it authenticates *data*, not a *session*. Good enough for "show me who this card belongs to"; not for login.
- **A2 — Challenge–response with the card's auth key (true login).** Server issues a nonce; the card signs it with the **auth key** via an `INTERNAL AUTHENTICATE` / `MSE:SET` + `PSO:Compute Digital Signature` APDU; server verifies with the auth cert's public key and validates the chain to Belgium Root CA + a fresh OCSP/CRL check. **PKCS#1 v1.5 is perfectly fine here** — it's an application signature, not a TLS handshake signature, so no TLS constraint applies. This is real proof-of-possession login, TLS-version-independent.
  - `EidReader` does **not** yet issue any signing APDU (`Card.cs` is read-only). A2 requires adding a `Sign(...)` path (MSE:SET security env + PSO). This is the main net-new work.

**The web catch:** the browser is the only component with card access for a *remote* user. So Option A for a web app needs a **local bridge** on the user's machine — one of:
- a small native helper exposing `https://localhost:<port>` / WebSocket that the page calls (the classic eID-applet successor pattern), or
- a browser extension bridging `navigator` ↔ PC/SC, or
- a desktop/WebView2 shell hosting both the reader and the page.

`EidReader` is Windows-only today (`WinSCard`); a cross-platform bridge would target PCSC-lite on Linux/macOS too.

### Option B — Federated identity (least code, production-grade)

Delegate to a government/broker IdP over OIDC/SAML — **CSAM / FAS**, **itsme**, or **eIDAS**. The eID is used inside the IdP's own flow; your app only speaks OIDC. TLS version is irrelevant, every browser works, no PC/SC code, no local helper. Cost: enrollment/contract with the IdP (CSAM registration or an itsme partner agreement) and you only get the attributes the IdP releases.

### Option comparison

| | Status quo (mTLS + TLS 1.2) | A1 read+verify | A2 challenge-response | B federated |
|---|---|---|---|---|
| Works on TLS 1.3 / any version | ❌ | ✅ | ✅ | ✅ |
| Supports new ECC cards | ❌ | ✅ | ✅ (add ECDSA sign) | ✅ |
| Proves card possession + PIN (login-grade) | ✅ | ❌ | ✅ | ✅ |
| Works for remote web users w/o local install | ✅ | ❌ (needs bridge) | ❌ (needs bridge) | ✅ |
| Net-new code | none | medium | high (sign APDU + bridge) | medium (OIDC) |
| Third-party enrollment | no | no | no | **yes** |

---

## 5. Recommendation

**Two-track, honest about the timeline:**

1. **Now — keep TLS 1.2 mTLS, and document it as intentional.** It is the correct and only design for RSA eID cards; TLS 1.2 is not deprecated and is safe for years. Add a code comment at `Program.cs:93` and `nginx.conf:10` pointing at this PRD so the pin is never mistaken for tech debt. *(This is the whole deliverable if the app stays a demo.)*
2. **When TLS-version-independence or ECC-card support actually becomes a requirement — go federated (Option B, itsme/CSAM).** It is the lightest path that fully removes the TLS coupling and is what real Belgian eID web logins use. Reserve **Option A2** for the specific case where you need direct card control with no third-party IdP (kiosk, desktop app, or an offline/edge scenario) — there, `EidReader` is the right foundation and A2 is the natural extension.

Do **not** invest in trying to make the *current mTLS design* span TLS versions — §2 proves that is impossible.

---

## 6. Implementation plan

### Track 1 — Document the pin & support both card types under TLS 1.2 (small, do now)

| # | Task | File(s) | Status |
|---|---|---|---|
| 1 | Comment linking `SslProtocols.Tls12` to this PRD (mandatory for PKCS#1-v1.5 eID cards; TLS 1.3 bans v1.5 CertificateVerify — RFC 8446 §4.2.3) | `Program.cs` | ✅ done |
| 2 | Comment on `ssl_protocols TLSv1.2;` marking the pin as intentional | `nginx.conf` | ✅ done |
| 3 | Broaden `SignatureAlgorithms` to add `ECDSA+SHA*` (support ECC cards, still no PSS) | `nginx.conf` | ✅ done |
| 4 | **Add the current Belgian Citizen CA(s) — and any newer root — to the client-CA bundle** so Firefox offers ECC cards. Source: `repository.eid.belgium.be` (Root + Citizen listings) | `traefik-dynamic-config/belgian-eid-cas.pem` | ⏳ needs CA files |
| 5 | Note the RSA/ECC support + CA-bundle requirement | `Readme.md` | ⏳ |
| 6 | (opt.) Detect the empty-cert / handshake-fail case and render a clear "use Chrome, or a TLS-1.2 client" message instead of a hang | `Program.cs` | ⏳ |

### Track 2 — Federated fallback spike (Option B, when required)

| # | Task | Effort |
|---|---|---|
| 5 | Register a test client with CSAM/FAS **or** obtain an itsme sandbox; capture issuer, scopes, released attributes | medium (mostly process) |
| 6 | Add OIDC auth (`AddOpenIdConnect`) alongside the existing cookie scheme; map claims to `PersonInfo` | small |
| 7 | Route `/login` to OIDC; keep mTLS route for backward-compat during migration | small |
| 8 | Remove the nginx-eid shim + TLS-1.2 pin **only after** OIDC is the default and mTLS is retired | small |

### Track 3 — Application-layer challenge-response (Option A2, only if no-IdP direct-card control is needed)

| # | Task | File(s) | Effort |
|---|---|---|---|
| 9 | Add a signing APDU path to the reader: `MSE:SET` (auth key ref) + `PSO:Compute Digital Signature`; expose `EidCard.SignWithAuthKey(byte[] challenge)` | `MintPlayer.EidReader/Card.cs`, `EidCard.cs` | high |
| 10 | Server nonce endpoint + verify (public key from `AuthCert`, chain to Belgium Root CA4, OCSP/CRL) | `DemoWebApp` | medium |
| 11 | Local bridge (localhost HTTPS/WebSocket helper, or WebView2 shell) exposing read + sign to the page | new project | high |
| 12 | Cross-platform PC/SC (PCSC-lite) if non-Windows clients matter | `EidReader.Native` | high |
| 13 | Add ECDSA sign support so applet-≥1.8 cards work | reader + verify | medium |

---

## 7. Testing / acceptance

- **Track 1:** comments present; `openssl s_client -tls1_3 -connect eid.mintplayer.com:443` fails to negotiate client auth (expected), `-tls1_2` still returns `PersonInfo` in Chrome **and** Firefox; Readme states the ECC-card limitation.
- **Track 2:** OIDC login returns the same `PersonInfo` fields in Chrome, Firefox, and Safari over **TLS 1.3** (verify `Protocol: TLSv1.3` in `openssl`/devtools); no client cert involved.
- **Track 3:** with a card inserted, `/login` completes a nonce round-trip over a **TLS 1.3** connection; tamper test (flip one challenge byte) is rejected; revoked-cert test is rejected.

---

## 8. References

- RFC 8446 (TLS 1.3) §4.2.3 — `rsa_pkcs1_*` not defined for handshake signatures; RSA `CertificateVerify` must be PSS
- RFC 5246 (TLS 1.2) §7.4.4 / §7.4.8 — server-offered `signature_algorithms`; RSA `CertificateVerify` = PKCS#1 v1.5
- `eid-mw/cardcomm/pkcs11/src/cardlayer/card.cpp:870-887, 895-964` — applet-version signing-capability gate; PSS = applet 1.7 only
- `eid-mw/cardcomm/pkcs11/src/cal.cpp:445-558, 2306-2358` — dynamic `C_GetMechanismList`; mechanism→algo mapping
- Mozilla bug 1588941 / 1611521 — NSS: non-PSS token + TLS 1.3 ⇒ empty cert, auth fails (incl. post-handshake auth)
- CVE-2019-11727 (Mozilla bug 1552208) — NSS PKCS#1-v1.5 in TLS 1.3 removed as a security bug
- [golang/go#45266](https://github.com/golang/go/issues/45266) — Go `crypto/tls` won't expose `SignatureAlgorithms` (why Traefik can't do the nginx trick)
- Companion: [`FIREFOX-MTLS-PSS-ROOTCAUSE.md`](FIREFOX-MTLS-PSS-ROOTCAUSE.md), [`Readme.md`](Readme.md)
