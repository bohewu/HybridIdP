# SAML counterpart demo

A standalone ASP.NET Core MVC app with two independently disabled roles:

- Consumer (SP): sign in through HybridIdP and display its validated issuer and
  persistent opaque subject and bounded received attributes in a local sample session.
- Producer (IdP): authenticate one operator-configured synthetic identity, then
  return a signed response to HybridIdP's external-login callback.

The existing `samples/TestClient` remains the OIDC consumer. This app does not
reference the production host or change its authentication defaults.

## Prerequisites

- .NET 10 SDK and **ASP.NET Core / .NET runtime 10.0.13 or later patched 10.0.x**.
  Check the runtime selected by your host with `dotnet --list-runtimes`.
  The explicit `System.Security.Cryptography.Xml` 10.0.13 package floor cannot
  patch an older shared runtime: .NET 10 can prune framework package assets.
  Use an isolated patched runtime/container if the host is older; do not infer
  runtime security from a successful restore or build.
- Trusted HTTPS hosting certificates for both applications. For local
  development, provision/trust an ASP.NET development HTTPS certificate through
  your normal operator process. Never bypass browser or HTTP TLS validation.
- Separate RSA signing certificates with at least 2048-bit keys and digital
  signature usage for each enabled role. Generate/provide them externally.
  Use password-protected PFX files for local private keys and DER/PEM public
  certificates for partner trust. Use absolute paths outside the checkout.
- A separately configured HybridIdP with its required database/migration and
  Data Protection setup. See the repository SAML integration documentation.

Use distinct local HTTPS ports. The generic examples below use HybridIdP at
`https://localhost:7443` and this sample at `https://localhost:7445`. Match the
actual deployment URLs exactly, including case, port, path and trailing slash.

## Private configuration

Both roles are off in the committed `appsettings.json`. For local development,
create a private JSON file **outside the repository**, using the following
shape. The values in angle brackets must be replaced; they are not credentials.
Set only the roles required for your journey to `true`.

```json
{
  "Saml": {
    "Sp": {
      "Enabled": true,
      "EntityId": "https://localhost:7445/sp",
      "AssertionConsumerServiceUrl": "https://localhost:7445/saml/sp/acs",
      "SigningCertificate": {
        "PfxPath": "<absolute path to sample SP PFX>",
        "Password": "<password>"
      },
      "IdentityProvider": {
        "EntityId": "https://localhost:7443/idp",
        "SingleSignOnUrl": "https://localhost:7443/saml/idp/sso",
        "SigningCertificatePaths": ["<absolute path to HybridIdP IdP public certificate>"]
      }
    },
    "Idp": {
      "Enabled": true,
      "EntityId": "https://localhost:7445/idp",
      "SingleSignOnUrl": "https://localhost:7445/saml/idp/sso",
      "SigningCertificate": {
        "PfxPath": "<absolute path to sample IdP PFX>",
        "Password": "<password>"
      },
      "ServiceProviders": [{
        "EntityId": "https://localhost:7443/sp",
        "AssertionConsumerServiceUrl": "https://localhost:7443/saml/sp/acs",
        "SigningCertificatePaths": ["<absolute path to HybridIdP SP public certificate>"]
      }]
    }
  },
  "DemoIdentity": {
    "AllowSyntheticIdentity": true,
    "Username": "<operator-selected synthetic username>",
    "Password": "<password>",
    "Subject": "<stable nonempty GUID in 32 hexadecimal character N format>"
  }
}
```

Replace each `<password>` placeholder with its private value. Choose a random
`DemoIdentity:Password` of at least 16 characters.

Keep `DemoIdentity:Subject` stable across restarts: it is the producer's persistent
NameID, independent of the entered username. Changing subject or either entity
ID creates a different external identity. Do not supply real user passwords or
reuse a real account for this sample. The producer fails startup outside
`Development` or `Test`, without explicit opt-in, or without valid demo settings.

Import the private file into the sample's development user-secrets store from a
PowerShell terminal. `user-secrets` is local development storage, not an
encrypted production secret store. Protect its file permissions accordingly.

```powershell
$privateSampleSettings = '<absolute path outside the checkout to private JSON>'
Get-Content -LiteralPath $privateSampleSettings -Raw |
  dotnet user-secrets set --project samples/TestClient.Saml
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project samples/TestClient.Saml --no-launch-profile --urls https://localhost:7445
```

In `Test`, default ASP.NET configuration does not automatically load development
user secrets. Supply equivalent `Saml__...` and `DemoIdentity__...` environment
variables through your private runtime configuration. Never commit the private
file, secrets store, PFX files, recordings or local overrides.

## Trust exchange and browser journeys

### Sample consumer -> HybridIdP identity provider

Configure HybridIdP's `Saml:Idp` role separately:

| HybridIdP setting | Value matching the example |
| --- | --- |
| `Enabled` | `true` |
| `EntityId` | `https://localhost:7443/idp` |
| `SingleSignOnUrl` | `https://localhost:7443/saml/idp/sso` |
| `SigningCertificate:PfxPath` / `Password` | HybridIdP IdP private signing material |
| `ServiceProviders:0:EntityId` | `https://localhost:7445/sp` |
| `ServiceProviders:0:AssertionConsumerServiceUrl` | `https://localhost:7445/saml/sp/acs` |
| `ServiceProviders:0:SigningCertificatePaths:0` | Sample SP public certificate |

1. Start both HTTPS apps with these settings and externally provisioned keys.
2. Open the sample `/` demo center and select **Sign in with HybridIdP**.
3. Complete HybridIdP's normal local login and any required local MFA.
4. Return to the sample `/session`. **Identity validated** shows the trusted
   issuer, persistent subject and validation time. **Received SAML attributes**
   shows bounded names and values from the validated assertion, HTML-encoded
   for inspection only. Raw assertions and tokens are not displayed.
5. **Sign out of this sample** clears only the sample cookie; the HybridIdP
   session may remain active. A subsequent sign-in may reuse that IdP session.

HybridIdP releases its local user ID in N format as persistent NameID and one
fixed `urn:hybrididp:claims:subject` attribute. The sample does not reinterpret
that subject as an email or use presentation claims to bind identities.

To inspect additional attributes, open HybridIdP's `/Admin/SamlAttributes` as an
authorized operator, select the configured sample SP, save its selection, then
preview an account. The page requires `settings.read`, saving requires
`settings.update`, and account search/preview separately requires
`claims.preview`. Preview uses saved policy only; save or discard edits first.
The [SAML integration guide](../../docs/SAML.md#outgoing-attribute-selection-and-preview)
lists the 16 local account/Person fields, output naming rules, value limits and
permissions. With unchanged account/source data and saved policy, compare the
preview to a fresh sample sign-in after allowing up to five minutes for existing
Settings caches on other instances. No new environment keys or migration are
needed for attribute selection; deployment-owned partner trust setup is still
required. An absent or empty policy keeps the fixed-subject-only release.

The sample extracts attributes only from the same assertion after both
signatures and protocol checks pass. After browser correlation and replay
reservations succeed, the display data is retained in protected authentication
properties, separate from the local identity claims. Received role-, permission-
or MFA-shaped names grant no local roles, permissions or MFA assurance. The
sample session identity remains the persistent subject, validated issuer and
validation time.

Display limits are 32 attributes, 256 UTF-16 code units per name, 8 values per
attribute, 1024 UTF-16 code units per value, and 2048 bytes for serialized UTF-8
JSON display data including escaping. Entries with nested XML, missing names or
values, or exceeded limits are omitted as whole attributes; an explicit notice
marks omissions. These display limits do not change the validated identity or
relax signed-response validation. HybridIdP can release values up to 2048 code
units, so a valid issued value may exceed the sample's smaller display limit.
An older session without attribute display data asks you to sign out and sign
in again. The bounded display is not a full assertion dump or evidence of every
released value when omissions are marked.

### HybridIdP service provider -> sample identity provider

Configure HybridIdP's `Saml:Sp` role separately:

| HybridIdP setting | Value matching the example |
| --- | --- |
| `Enabled` | `true` |
| `EntityId` | `https://localhost:7443/sp` |
| `AssertionConsumerServiceUrl` | `https://localhost:7443/saml/sp/acs` |
| `DisplayName` | `SAML demo identity` |
| `SigningCertificate:PfxPath` / `Password` | HybridIdP SP private signing material |
| `IdentityProvider:EntityId` | `https://localhost:7445/idp` |
| `IdentityProvider:SingleSignOnUrl` | `https://localhost:7445/saml/idp/sso` |
| `IdentityProvider:SigningCertificatePaths:0` | Sample IdP public certificate |

1. Start at HybridIdP's normal login page and select its enabled SAML provider.
2. On this sample's **Authenticate the demo identity** screen, enter the exact
   private `DemoIdentity:Username` and `Password` selected by your operator.
3. The sample validates the signed request, authenticates that one identity,
   and POSTs a signed response and assertion to the pinned HybridIdP ACS.
4. Continue through HybridIdP's existing external account confirmation or
   explicitly initiated linking flow and any local MFA/policy checks. The
   sample does not grant account eligibility, binding or local MFA assurance.

The producer intentionally supplies no email/name fields. A first login can
require HybridIdP's normal local account completion. Returning logins use the
same durable external subject. It does not mint identities from arbitrary
usernames or retain a producer login session; each request requires the
configured password, including a ForceAuthn request.

## Routes and supported profile

| Route | Purpose |
| --- | --- |
| `GET /` | Demo center and independent role status |
| `POST /saml/sp/login` | Antiforgery-protected consumer sign-in |
| `POST /saml/sp/acs` | Signed POST response validation; browser correlation replaces form antiforgery |
| `GET /session` | Validated consumer identity and bounded received attributes |
| `POST /logout` | Antiforgery-protected local consumer logout |
| `GET /saml/sp/metadata` | Consumer public signing certificate and ACS |
| `GET /saml/idp/metadata` | Producer public signing certificate and SSO URL |
| `GET /saml/idp/sso` | Signed Redirect request validation |
| `GET, POST /saml/idp/continue` | Browser-bound producer password form and one-use issuance |

Disabled roles cannot initiate or accept authentication. Signing trust is pinned locally, with no
online metadata fetching or request-supplied ACS/key authority. Public metadata
is for operator inspection; exchange public certificate files through your
approved out-of-band channel and configure the pins explicitly.

The profile is SP-initiated signed HTTP-Redirect AuthnRequest, HTTP-POST response
**and** assertion signatures, RSA-SHA256 signatures, SHA256 digests, and persistent
NameID. NameQualifier is the IdP entity; SPNameQualifier is the SP entity. Input
qualifiers are optional, but must match those exact identities when present.
Issuer, ACS destination, bearer recipient, audience, InResponseTo and temporal
windows are validated. Message lifetime is five minutes, maximum skew one
minute; replay markers last up to ten minutes. XML/body sizes are bounded and
DTDs are prohibited. Assertions are unencrypted and require HTTPS throughout.

This is an explicitly single-process development sample: correlation, replay
and continuation state live in a bounded in-memory store with atomic one-use
consumption and protected Secure/HttpOnly browser cookies. Restart invalidates
pending flows. Do not run replicas or use it as a production federation service.
Password attempts are limited to five per minute per direct remote IP. It is
not a production credential system. No reverse-proxy trust is configured.

Unsolicited IdP login, federated SLO, assertion encryption, metadata discovery,
dynamic partner management and production account stores are outside this demo.
Local sign-out does not revoke upstream or downstream sessions.

## Focused verification

```powershell
dotnet build samples/TestClient.Saml/TestClient.Saml.csproj
dotnet test samples/TestClient.Saml.Tests/TestClient.Saml.Tests.csproj
```

The tests exercise the sample's actual signed protocol primitives, tamper/key
rejection, one-use browser state, protected-cookie attribute display, encoded
rendering, display limits and display-only claim semantics using ephemeral test
keys. These automated checks and builds do not
establish connected-browser success, HybridIdP account policy acceptance,
rendered UI quality, runtime patch status or OIDC compatibility. Those require
separate connected journeys against the configured applications.

Connected checks passed in an isolated, single-node SQL Server 2019 environment:
per-application save/reopen and isolation, absent/empty-policy fallback, and
saved-preview equality with the Sample's validated signed attributes, preserving
NameID, qualifiers and fixed subject. The unchanged OIDC sample completed PAR,
authorization-code/token exchange and protected-session reload. Current assets
were checked at 390x844 and 1440x1000. Two private browser clips were played to
the end; functional footage predates the final mobile CSS correction, whose
current assets were checked separately. This evidence does not establish
production, multi-instance cache, performed-MFA or full role off/on acceptance.
