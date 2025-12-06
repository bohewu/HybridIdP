# OAuth 2.0 Flows

This document describes the supported OAuth 2.0 / OpenID Connect flows in HybridIdP.

## Supported Flows

### 1. Authorization Code Flow (with PKCE)
**Standard for:** SPAs, Mobile Apps, Web Apps.
**Grant Type:** `authorization_code`
**Response Type:** `code`

The most secure flow for user-centric applications. Requires PKCE (Proof Key for Code Exchange).
1. Client redirects user to `/connect/authorize`.
2. User authenticates and consents.
3. Code is returned to `redirect_uri`.
4. Client exchanges code for generic tokens at `/connect/token`.

### 2. Client Credentials Flow
**Standard for:** Machine-to-Machine (M2M), Service Accounts, Daemons.
**Grant Type:** `client_credentials`

Used when the application acts on its own behalf, not a user.
- **Restrictions:** M2M clients cannot request user-centric scopes (`openid`, `profile`, `email`, `roles`).
- **Authentication:** Client ID + Client Secret.

### 3. Device Authorization Flow
**Standard for:** CLI tools, TV apps, IoT devices (no browser / limited input).
**Grant Type:** `urn:ietf:params:oauth:grant-type:device_code`

1. Device requests code from `/connect/device`.
2. Device displays `user_code` and `verification_uri` (`/connect/verify`).
3. User visits URI on another device (phone/laptop) and enters code.
4. Device polls `/connect/token` until user approves.

### 4. Refresh Token Flow
**Standard for:** Renewing access tokens without re-authentication.
**Grant Type:** `refresh_token`

- **Policy:** Rolling refresh tokens (new RT issued with every use).
- **Lifetime:** Configurable (default 14 days).

## Deprecated / Removed Flows

### Implicit Flow
**Status:** **REMOVED**
Legacy flow returning tokens in URL. Replaced by Authorization Code + PKCE.

### Resource Owner Password Credentials (ROPC)
**Status:** Supported but **NOT RECOMMENDED**.
Only for legacy migration or highly trusted legacy clients.

## Endpoints

| Endpoint | Path | Method | Description |
|----------|------|--------|-------------|
| Authorization | `/connect/authorize` | GET/POST | User interactive login |
| Token | `/connect/token` | POST | Token issuance |
| Introspection | `/connect/introspect` | POST | Token validation (M2M) |
| Revocation | `/connect/revoke` | POST | Token revocation |
| Device Auth | `/connect/device` | POST | Device flow initiation |
| Verification | `/connect/verify` | GET/POST | Device flow user input |
| UserInfo | `/connect/userinfo` | GET/POST | User profile data |

## Testing

See `DEVELOPMENT_GUIDE.md` for details on how to use `TestClient` or `curl`/Postman to test these flows.
