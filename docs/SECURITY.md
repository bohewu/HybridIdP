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
- **Features**: Background queue processing (non-blocking), rate-limiting, and short-lived expiry.

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
