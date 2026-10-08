# OAuth 2.0 Flows

This document describes the supported OAuth 2.0 / OpenID Connect flows in HybridIdP.

API scopes require current resource usage approval at token issuance. Stored
client scope permissions and end-user consent alone do not establish it. Every
mapped API resource must be explicitly open or have a server-recorded approval
for that client and scope from its owner or a full IdP Admin. The scope's catalog
visibility and OIDC/M2M `IsPublic` classification are separate decisions.
Authorization-code, refresh, device, client-credentials and password issuance
return the normal machine-readable OAuth `invalid_scope` error through the
OpenIddict server scheme if usage approval is missing. Current audiences are
recomputed on successful issuance. Discovery and supported grants are unchanged.

Existing and seeded sample API clients require explicit approval after upgrade;
their permissions and credentials are preserved. Approve each mapped resource
in its API Resource editor, then configure the client's allowed scopes. Unmapped
custom scopes can be approved in the Scope editor. Ownerless or unknown resource
ownership requires full Admin approval; no legacy permission is auto-approved.

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

Manual code entry first resolves the application and displays its requested
scopes; it does not grant access. The user then allows the displayed scope set
or denies the request. A complete verification URI opens that same review
directly. Approval and denial require a one-time intent bound to the user,
client and code. Explicit/systematic clients require an affirmative decision;
external-consent clients additionally require a valid permanent authorization
covering the requested scopes. Implicit clients retain their trusted consent
policy. Denial causes device polling to return OAuth `access_denied` as JSON.

When `RateLimiting:Enabled` is true, native device issuance uses the existing
token budget and verification uses the existing authorize budget, each keyed
by trusted source IP in separate partitions before OpenIddict processing.
Issuance rejection returns HTTP 429 with OAuth `temporarily_unavailable` JSON.

Approval and device-code redemption each evaluate the current global mandatory
MFA policy as well as the client's `RequireMfa` setting. Either requirement
needs performed `amr=mfa` evidence; `hwk` alone is insufficient. A password-only
cookie cannot approve after policy activation, and an approval persisted before
activation cannot redeem without that evidence. Enrollment grace does not
grandfather a device authorization. A rejected redemption returns OAuth
`invalid_grant` as JSON through the OpenIddict server scheme. Compliant MFA
retains the existing one-time approval intent, current account/Person and
migration eligibility, scope permissions and API usage approval checks.

### 4. Refresh Token Flow
**Standard for:** Renewing access tokens without re-authentication.
**Grant Type:** `refresh_token`

- **Policy:** Rolling refresh tokens (new RT issued with every use).
- **Lifetime:** Configurable (default 14 days).

## Deprecated / Removed Flows

External-consent clients require a valid permanent authorization covering all
requested scopes after client policy filtering. Both authorize GET and POST
reuse that approval; interactive consent cannot create an External grant.
Missing approval returns the OpenIddict `consent_required` protocol error.

Current Explicit and Systematic consent policies require a new approval on each
authorization request, including after a change from Implicit with an existing
Permanent grant. Such grants cannot bypass the current policy. A silent
`prompt=none` request that needs approval returns `consent_required`; it never
renders an interactive consent page. Implicit grant reuse and prior External
approval remain available under their respective policies.

### Implicit Flow
**Status:** **REMOVED**
Legacy flow returning tokens in URL. Replaced by Authorization Code + PKCE.

### Resource Owner Password Credentials (ROPC)
**Status:** Supported but **NOT RECOMMENDED**.
Only for legacy migration or highly trusted legacy clients.

## Endpoints

### OIDC end-session confirmation

`/connect/logout` accepts initial RP GET and POST requests without requiring
local antiforgery. An authenticated application cookie is retained until the
user submits the local confirmation form with valid antiforgery bound to that
browser/user. Missing, invalid or another browser's confirmation cannot sign
out the user. POST-carried client, id-token, post-logout redirect, state and
optional logout/UI-locale parameters survive the confirmation roundtrip.
Unauthenticated completion retains the existing behavior. OpenIddict still
validates client permissions and registered post-logout redirects; local
confirmation grants no redirect authority. Discovery retains
`end_session_endpoint=/connect/logout`. Focused local fixtures do not establish
a rendered browser/RP roundtrip or deployed endpoint acceptance.

| Endpoint | Path | Method | Description |
|----------|------|--------|-------------|
| Authorization | `/connect/authorize` | GET/POST | User interactive login |
| Token | `/connect/token` | POST | Token issuance |
| Introspection | `/connect/introspect` | POST | Token validation (M2M) |
| Revocation | `/connect/revoke` | POST | Token revocation |
| Device Auth | `/connect/device` | POST | Device flow initiation |
| Verification | `/connect/verify` | GET/POST | Device flow user input |
| UserInfo | `/connect/userinfo` | GET/POST | User profile data |
| End Session | `/connect/logout` | GET/POST | OIDC ingress and browser-bound local confirmation |

## Testing

See `DEVELOPMENT_GUIDE.md` for details on how to use `TestClient` or `curl`/Postman to test these flows.
