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

The recommended flow for user-centric applications. Public clients require PKCE
(Proof Key for Code Exchange); confidential clients have a per-client requirement
setting as described below.
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

## Client settings and protocol permissions

The client editor selects `public` or `confidential` when creating a client.
Public clients (for example a browser SPA) cannot keep a client secret; their
PKCE requirement is always enabled. Confidential clients (for example a server
web application that securely holds its secret) expose a **Require PKCE**
checkbox. New confidential clients default to requiring PKCE; an update omitting
that field preserves their existing requirement. Native applications must be
public. Application type `web` alone does not mean confidential.

The checkbox persists OpenIddict's `ProofKeyForCodeExchange` requirement. It
does not select a challenge algorithm or enforce S256-only. Configure the
downstream OAuth client to send `code_challenge_method=S256`, its derived
`code_challenge`, and the matching `code_verifier` at code redemption. The admin
authorization URL helper generates S256 requests. There is no per-client
challenge-method selector in the current editor or DTO.

`client_secret_basic` authenticates a confidential client at `/connect/token`
using HTTP Basic over HTTPS. Configure that method in the downstream client
library and keep its registered secret on the server. PKCE is an additional
code-redemption check, not a replacement for confidential-client authentication.
There is no current per-client token authentication method selector or setting
that restricts a client to Basic only. A registration with `type=public` and
`clientSecret=null` has no confidential secret to use for Basic. Changing the
downstream method alone cannot convert that registration; any migration must
be deliberate. The editor does not expose client-type changes on existing
registrations.

| Setting | Server web application | Browser SPA |
|---------|------------------------|-------------|
| Registration type | `confidential`, application type `web` | `public`, application type `web` |
| Secret | Generated on creation; store securely on the backend | No secret |
| Require PKCE | Checked | Always required |
| Downstream PKCE method | `S256` | `S256` |
| Downstream token authentication | `client_secret_basic` | No client authentication (`none` in client libraries); send `client_id` |
| Authorization request | `response_type=code` | `response_type=code` |
| Client permissions | `ept:authorization`, `ept:token`, `gt:authorization_code` | Same |
| Redirect URI | Register the exact server callback URI | Register the exact SPA callback URI |
| Identity scopes | Allow and request `openid profile`; add `email` if needed | Same |

On create, and on update when permissions are supplied, ClientService adds
`OpenIddictConstants.Permissions.ResponseTypes.Code` when the authorization-code
grant permission is present. The editor has endpoint/grant permission checkboxes
and allowed scopes, but no separate response-type selector. Global server flow
enablement is still required; a checkbox in the generic permission list is not
proof that its flow is enabled. The current server enables authorization code
and does not enable the removed OAuth Implicit flow.

Endpoint, grant, response-type and scope permissions govern the OAuth application.
They do not grant the operator's `clients.create`/`clients.update` RBAC permissions,
end-user consent, or API resource usage approval. `ept:token` alone does not allow
every grant or scope. Allowing `profile` does not request or grant it; the
downstream application must request it and obtain approval under the consent
policy. Add `gt:refresh_token` and request `offline_access` only if refresh is
needed; add logout permission and an exact post-logout URI only if using logout.

A read-only local discovery snapshot on 2026-10-19 advertised response type
`code`, PKCE methods `plain` and `S256`, and token authentication methods
`client_secret_post`, `private_key_jwt` and `client_secret_basic`. This is local
advertisement evidence, not proof of every method's successful/rejected protocol
exchange, client credential provisioning, or deployed configuration. In
particular it does not establish S256-only or Basic-only enforcement, and does
not imply that public clients require a secret because `none` is absent from
that advertised list.

The client-type and Basic distinction follows
[RFC 6749 sections 2.1 and 2.3.1](https://www.rfc-editor.org/rfc/rfc6749#section-2.1).
The challenge/verifier relationship follows
[RFC 7636 section 4](https://www.rfc-editor.org/rfc/rfc7636#section-4).

## Requested scopes, Required and consent

Client **allowed** scopes define permission to request access. The **effective
request** contains only scopes actually requested and accepted by current client
permissions and API resource usage policy. **Approved** scopes are those granted
by the user or a valid approval under the client's consent policy. These sets
are distinct; an administrator adding a permission does not grant user consent.

```text
Required = effective request intersect (global Required union client Required)
```

Required applies only within the effective request. Consent GET displays these
items checked and unavailable for deselection; POST reloads the same metadata
and rejects removal before scope classification. Unrequested Required scopes are
neither displayed nor added, and their absence does not block login. Requested
optional scopes may be refused. Submitted extra scopes cannot enter the grant
or tokens beyond the effective request. Existing antiforgery, one-time consent
intent and client/subject/request bindings still apply.

For example, allowing `profile` and marking it Required does not add `profile`
to a client's request for `openid email`. Login can succeed without `profile`
or standard name claims. The client must actually request `profile`; only then
does Required prevent deselection.

### Existing authorizations and consent policies

Implicit **consent policy** retains its existing reuse behavior only for a valid
grant covering the effective request. It is distinct from the removed OAuth
Implicit flow. A new scope outside the old grant requires consent where user
approval is permitted; refused optional scopes are not issued. A larger old
grant cannot enlarge a smaller new request or carry its extra claims forward.

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
`prompt=consent` still requests confirmation under Implicit policy even when a
covering grant exists; it cannot replace the approval required by External.

OAuth protocol errors follow the endpoint's response mode: frontchannel
authorization errors may redirect to a validated client redirect URI, while
backchannel errors use machine-readable JSON. `prompt=none` does not render a
consent page; a required interaction returns `consent_required`.

### Refresh, claims and revocation

Refresh cannot expand the original grant; expansion returns `invalid_scope`.
Omitting `scope` does not acquire administrator-added permissions. Narrowing a
refresh request or removing client permissions filters newly issued scopes and
their dependent claims. A custom claim shared by several scopes remains when
another lawful granted scope still supplies it; basic protocol claims remain.

Standard `name` and `preferred_username` require granted `profile` in new ID and
access tokens and UserInfo. A custom `employee-id` scope does not imply
`profile`, even if a custom mapping names those claims. `sub` remains the local
UserId. UserInfo returns flat claims, without a `profile` JSON wrapper.

Revoking an OAuth application authorization prevents its reuse and invalidates
related refresh through existing revocation checks. Removing an upstream login
link is a separate operation. These checks apply to new issuance; existing
self-contained access tokens are not rewritten and may remain usable until
their normal expiry or a resource's applicable validation/revocation checks.

### Existing-client upgrade notes

This consent correction requires no client recreation, registration/schema
migration, credential rotation or PKCE configuration change. In the actual
v1.4.3 format, client Required settings already lived in `ClientRequiredScopes`,
separate from allowed permissions; absent rows mean no client-specific Required
scopes. Representative legacy public PKCE, confidential PKCE and confidential
non-PKCE descriptors pass focused current-service fixtures. This is source and
fixture evidence, not proof for every configuration registered on older releases.

For a cumulative upgrade from v1.4.3 or earlier, consumers relying on historical
`name` disclosure without `profile` must allow, request and obtain `profile`,
then obtain fresh tokens. Existing API-resource policy migrations also introduce
closed defaults without backfilling usage approvals: mapped custom/API resources
need an open policy or current approval. Standard unmapped identity scopes are
exempt from that approval requirement. These are configuration-specific upgrade
considerations, not a requirement to replace every old client. See the
[Profile upgrade guidance](DEPLOYMENT_GUIDE.md#unified-api-login-and-profile-upgrade)
and [API usage approval rules](SECURITY.md).

## Deprecated / Removed Flows

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

### MFA assurance after factor changes

Newly completed MFA carries an internal security-stamp binding. Authorization
GET and consent POST compare it to the current account before accepting MFA;
authorization-code exchange, refresh and device approval/exchange do the same.
The binding remains in protected protocol grants and has no access-token or
ID-token destination. A client or global MFA requirement rejects missing/stale
binding. Optional token issuance strips obsolete MFA AMR/ACR. An original
`acr_values=mfa` grant also retains its requirement during code exchange/refresh.

With optional global MFA, authorized final-factor removal keeps primary account
access and clears obsolete MFA and management proof. A later required client
routes to fresh verification or replacement setup. A fresh independent account
code may authorize the explicitly selected enrollment-only recovery ceremony,
including its last code; legacy OTP backups do not gain this authority. Existing
access tokens expire normally; this change does not revoke already-issued tokens.
