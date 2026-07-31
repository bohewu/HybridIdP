# Security Policy

## Overview
HybridAuthIdP is committed to maintaining a high level of security. This document outlines our multi-factor authentication (MFA) implementations, security hardening practices, and how to report vulnerabilities.

## Supported Multi-Factor Authentication (MFA)

We support three primary MFA methods to ensure account security:

### 1. TOTP (App-based)

- **Standard**: RFC 6238 compliant.
- **Compatible Apps**: Google Authenticator, Microsoft Authenticator, Authy, etc.
- **Features**: Recovery codes (10 backup codes), rate-limiting on verification attempts.

### 2. Email OTP

- **Standard**: 6-digit one-time code sent via email.
- **Generation and storage**: Codes use a cryptographically secure random-number generator and are stored only as password hashes.
- **Verification budget**: Each pending code permits at most five verification attempts. The fifth failed attempt invalidates that code; sending a replacement code starts a new budget.
- **Features**: Background queue processing (non-blocking), send rate-limiting, and a 10-minute expiry.

### 3. Passkey (WebAuthn)

- **Standards**: FIDO2 / WebAuthn.
- **Authenticators**: Biometrics (Windows Hello, Touch ID, Face ID) and hardware keys (e.g., YubiKey).
- **Security Policy**: Configurable "Strong MFA Prerequisite" (requires existing TOTP/Email MFA before registering a Passkey).

---

## Security Hardening Implementation

We implement several defense-in-depth measures:

### Security Headers
The system enforces strict security headers via `SecurityHeadersMiddleware`:
- **Content-Security-Policy (CSP)**: Strict policy blocking inline styles/scripts (`unsafe-inline` is prohibited in production).
- **HSTS**: Strict Transport Security enforced for 1 year.
- **X-Frame-Options**: Set to `DENY` to prevent clickjacking.
- **Permissions-Policy**: Disables camera, microphone, and geolocation by default.

### Cookie Security
All authentication and session cookies are configured with:
- `HttpOnly`: Prevents access from JavaScript.
- `Secure`: Transmitted only over HTTPS.
- `SameSite`: Set to `Lax` or `Strict` for CSRF protection.

### Production Deployment Inputs and Network Boundary

Production compose requires operator-managed, non-empty database connection strings, database initialization passwords for modes with internal databases, and certificate passwords. The setup scripts generate the required values; production compose has no built-in MSSQL or PostgreSQL password fallback.

Validation rejects missing or empty required inputs before image pull, local build, or service startup. Its diagnostics name the missing variable but never print the configured value. Store and provide these values through the operator's approved secret-management process.

MSSQL, PostgreSQL, and Redis have no host-published ports in the default production compose configuration. `deployment/docker-compose.local-ports.yml` is the explicit, loopback-only diagnostic override for the internal data services; it must not be treated as a production default.

### One-Time Operational First Administrator

`OperationalAdminBootstrap` is absent in effect until an operator explicitly enables it; it is disabled by default and is only for a genuinely fresh deployment. It is not a replacement for the fixed Development/Test privileged test fixture or for normal authenticated administrator management after sign-in.

The only accepted capability is a 43-character base64url token supplied in the `X-HybridAuth-Bootstrap-Token` request header over HTTPS. Configuration contains only its hex-encoded SHA-256 digest and an absolute UTC expiry (`OperationalAdminBootstrap__TokenSha256Digest` and `OperationalAdminBootstrap__ExpiresAtUtc`), plus the explicit `OperationalAdminBootstrap__Enabled` switch. The raw token must never appear in configuration, source control, a URL or query string, logs, shell history, or a process list.

When TLS terminates at a proxy, forwarded HTTPS is trusted only through the existing `Proxy__Enabled` and `Proxy__KnownProxies` trust model: specific proxy IPs or CIDRs become the forwarding middleware's known-proxy/known-network set. The caller source IP is only a rate-limiting partition and is never authorization. The completion marker is system-owned, so the ordinary settings API cannot alter it. See [the deployment workflow](DEPLOYMENT_GUIDE.md#one-time-operational-first-administrator) for the required first-use and cleanup procedure.

---

## Reporting a Vulnerability

If you discover a security vulnerability within this project, please report it to us as soon as possible.
- **Email**: [security@hybridauth.local](mailto:security@hybridauth.local) (placeholder)
- **Response Time**: We aim to acknowledge reports within 48 hours and provide a timeline for fixes.

Please do not disclose the vulnerability publicly until we have had a chance to address it.

---

## Security Hardening (詳解)

### Content Security Policy (CSP)
系統實施嚴格的 CSP 策略以防止 XSS 攻擊：
- 禁用 `unsafe-inline` 樣式與腳本。
- 僅允許來自信任 CDN (jsdelivr, Cloudflare Turnstile) 的資源。

### Secure Cookies
所有認證與會話 Cookie 皆具備：
- `HttpOnly`: 防止 JS 存取。
- `Secure`: 僅限 HTTPS 傳輸。
- `SameSite=Lax/Strict`: 防止 CSRF 攻擊。

---

## Scope-Based Authorization (範圍授權服務)

### 概述
HybridIdP 實作 OAuth 2.0 範圍授權，透過 `RequireScope:` 策略模式保護 API 端點。

### 使用方式
在 Controller 或 Action 上方加入屬性：
```csharp
[Authorize(Policy = "RequireScope:api:company:read")]
```

### 強制性範圍 (Required Scopes)
管理者可設定特定 Client 必須具備的 Scope（如 `openid`），使用者在授權頁面無法取消勾選這些範圍。

---
**Last Updated**: 2025-12-19
