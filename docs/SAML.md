# SAML integration

HybridIdP can independently act as a SAML identity provider (IdP) and service
provider (SP), alongside existing OIDC. Both roles default to disabled. Enabling
one does not enable the other or change existing OIDC client settings, scopes,
redirect URIs, consent or authentication defaults. SAML partners are deployment
configuration, separate from OIDC clients; there is no partner administration UI.

## Supported profile

ITfoxtec.Identity.Saml2 and MvcCore 4.21.0 implement this bounded profile:

- SP-initiated, signed HTTP-Redirect AuthnRequest; HTTP-POST response with both
  response and assertion signed. RSA-SHA256 signatures and SHA256 digests only.
- Persistent NameID; exact configured HTTPS issuer, destination, ACS, audience,
  bearer recipient and request correlation. Five-minute messages, at most
  one-minute clock skew and server-side state lasting at most ten minutes.
- Offline, explicitly pinned partner signing certificates. Public metadata is
  informational; no online metadata retrieval or request-provided key/ACS trust.
- Unencrypted assertions over HTTPS. Assertion encryption, unsolicited
  IdP-initiated login and federated Single Logout are unsupported. Local logout
  clears only the relevant local session.

Require an actual ASP.NET Core/.NET **10.0.13 or later patched 10.0.x runtime**.
Check the runtime used by the process, not just the installed SDK. The
`System.Security.Cryptography.Xml` 10.0.13 package floor can be pruned against
.NET 10's shared framework and does not patch an older runtime. A successful
restore/build alone does not establish runtime patch status.

## Deployment configuration

Preserve an existing `.env`; add only the settings needed for the selected role.
This generic environment-file template deliberately leaves both roles off.
Replace paths/URLs with deployment values before enabling a role. Certificate
paths must be absolute in the application/container filesystem, outside Git.
Supply password keys through private environment injection or your secret
configuration provider; angle-bracket entries below are placeholders, not
built-in secret references and must not be used literally.

```dotenv
Saml__Idp__Enabled=false
Saml__Sp__Enabled=false

# IdP: this host issues to explicitly configured SPs.
Saml__Idp__EntityId=https://identity.example/idp
Saml__Idp__SingleSignOnUrl=https://identity.example/saml/idp/sso
Saml__Idp__SigningCertificate__PfxPath=/run/secrets/idp-signing.pfx
Saml__Idp__SigningCertificate__Password=<inject-private-pfx-password>
Saml__Idp__ServiceProviders__0__EntityId=https://consumer.example/sp
Saml__Idp__ServiceProviders__0__AssertionConsumerServiceUrl=https://consumer.example/saml/sp/acs
Saml__Idp__ServiceProviders__0__SigningCertificatePaths__0=/run/secrets/consumer-signing.cer

# SP: this host accepts one configured upstream IdP.
Saml__Sp__EntityId=https://identity.example/sp
Saml__Sp__AssertionConsumerServiceUrl=https://identity.example/saml/sp/acs
Saml__Sp__DisplayName=SAML
Saml__Sp__SigningCertificate__PfxPath=/run/secrets/sp-signing.pfx
Saml__Sp__SigningCertificate__Password=<inject-private-pfx-password>
Saml__Sp__IdentityProvider__EntityId=https://upstream.example/idp
Saml__Sp__IdentityProvider__SingleSignOnUrl=https://upstream.example/saml/idp/sso
Saml__Sp__IdentityProvider__SigningCertificatePaths__0=/run/secrets/upstream-signing.cer
```

The colon configuration equivalents are `Saml:Idp:*` and `Saml:Sp:*`.
Lists use zero-based indexes: add `ServiceProviders__1__...` for another trusted
SP; add `SigningCertificatePaths__1` for a rollover key. Configure one to five
currently valid RSA certificates (at least 2048 bits) per partner. Local signing
uses a password-protected PFX with its private key; partner trust uses public
DER/PEM certificates. Exchange public certificates through an approved channel
and match entity IDs/URLs exactly, including case, path, port and trailing slash.
Certificate pinning does not disable HTTPS TLS validation. Keep private keys,
passwords, private configuration and recordings outside Git.

Docker Compose does not automatically forward arbitrary `.env` entries into a
container: explicitly map these keys in the service's private `environment` or
`env_file` configuration and mount certificate files read-only at the configured
paths. Use the established deployment process; do not regenerate existing secrets.

Before enabling either role, apply the selected database provider's additive
`SamlStates` migration through the normal backup/migration process:

| Database | Migration ID |
| --- | --- |
| SQL Server | `20261026010809_AddSamlStates` |
| PostgreSQL | `20261026010930_AddSamlStates` |

See [database configuration](DATABASE_CONFIGURATION.md) and
[deployment guidance](DEPLOYMENT_GUIDE.md). The migration adds protocol state,
not identity links or account backfills. With both roles disabled, SAML endpoints
return 404 and SAML state services do not query the table. Schema installation
alone never enables federation.

Enabled production roles use the shared application database for atomic one-use
correlation, continuation and replay state. All instances must share that database
and the same Data Protection application name/key ring, with durable protected
key storage. Keep clocks synchronized. Missing, expired, consumed, corrupt or
unavailable state denies the flow. Preserve the existing HTTPS/proxy settings;
the browser must retain Secure correlation cookies across the Redirect/POST
journey (SP correlation uses SameSite=None). Database and key-ring availability
are required; sticky routing alone is insufficient.

## Identity and local policy

As IdP, HybridIdP emits `ApplicationUser.Id` in GUID `N` format (32 hexadecimal
characters) as persistent NameID, with IdP/SP entity qualifiers. This stable
subject is **not pairwise**: the same local account ID is disclosed to configured
SPs. One fixed `urn:hybrididp:claims:subject` attribute repeats it. Email, names,
roles, permissions and upstream MFA claims are not released. Existing local
login, current account/Person eligibility, session/security stamp, lifecycle,
required password-change and performed local MFA checks apply before issuance.

As SP, the Identity login provider is `Saml`. Its durable provider key is
`saml-v1:` plus lowercase SHA256 of UTF-8 encoding of five length-prefixed strings:
trusted issuer, persistent NameID format URI, normalized NameQualifier,
normalized SPNameQualifier and exact opaque NameID. Each prefix is the invariant
decimal .NET string length (UTF-16 code units) followed by `:`. Missing qualifiers
normalize to the validated upstream IdP and local SP entity IDs; supplied
qualifiers must match exactly. No trimming, case folding or email substitution
is applied. Changing issuer, SP entity ID or subject changes the binding; retain
these values and the versioned key algorithm across upgrades.

Validated SAML creates only an external ticket. Existing external callbacks own
account completion, explicit linking, local MFA and current eligibility. Email
attributes do not automatically link an existing account; upstream AMR/roles
cannot grant local MFA assurance or authorization. An existing durable binding
takes precedence. Linking requires the existing locally protected account-link
flow. Existing client restrictions on external providers remain effective.

## Two sample journeys

Use the [standalone counterpart README](../samples/TestClient.Saml/README.md)
for the complete private configuration, certificate exchange and startup steps.
Configure reciprocal entity IDs, exact endpoints and public signing pins first.

1. **Sample SP -> HybridIdP IdP:** enable the sample consumer and HybridIdP IdP;
   start at the sample home page, sign in through HybridIdP's local login/MFA,
   and inspect the validated opaque subject on the sample session page.
2. **HybridIdP SP -> sample IdP:** enable HybridIdP SP and the sample producer;
   select SAML on HybridIdP's login page, authenticate the explicitly configured
   synthetic demo identity, then complete HybridIdP's normal external-account
   and local MFA steps.

The sample producer requires explicit synthetic-identity opt-in and
Development/Test. Its bounded in-memory one-use state supports one process only;
restart invalidates pending flows. Do not run sample replicas or treat it as a
production identity service. Existing `samples/TestClient` remains the OIDC demo.

Production metadata is at `/saml/idp/metadata` and `/saml/sp/metadata`; request
and callback endpoints are `/saml/idp/sso` and `/saml/sp/acs`. Metadata contains
public signing material only and does not automatically establish partner trust.

For acceptance, separately exercise both off, IdP only, SP only and both on;
confirm disabled-role endpoints cannot authenticate. Run both connected sample
journeys, then verify existing OIDC discovery, authorization-code/PKCE and token
behavior with SAML off and on. Unit tests and publish inspection do not establish
connected-browser interoperability or deployment acceptance.

The project remains MIT. Retain the [third-party notices](../THIRD_PARTY_NOTICES.md)
and full ITfoxtec BSD license in source and binary distributions.
