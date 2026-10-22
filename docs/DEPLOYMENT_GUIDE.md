# HybridIdP Deployment Guide

## Neutral non-login query configuration

The six capabilities below support explicit `legacy` and `neutral-v2` query
selection. With no saved selection, existing legacy behavior applies. Each
capability's `Enabled` flag and each `QuerySources` entry's `Enabled` and
`Trusted` flags default to false; source dictionaries default empty. Enabling a
query source does not enable login or authorize an operation. The normative
[2.0 contract](NEUTRAL_QUERY_CONTRACT.md) and per-capability schemas define the
wire; the current product supports the persisted-field subset described here.

| Capability / wire contract type | HybridIdP configuration prefix | Example producer path |
| --- | --- | --- |
| Native Profile | `ProviderNativeProfile` | `/api/neutral/native-profile` |
| Additional Profile properties | `ProviderProfile` | `/api/neutral/profile` |
| Metadata | `ProviderMetadataRefresh` | `/api/neutral/metadata` |
| Lifecycle | `ProviderLifecycle` | `/api/neutral/lifecycle-status` |
| Recovery Identity Verification | `RecoveryIdentityVerification` | `/api/neutral/recovery-identity-verification` |
| Password Sync | `LegacyPasswordSync` | `/api/neutral/password-sync` |

### Deployment ceilings and a copyable example

The private operator-managed `.env` owns complete endpoint URLs, shared secrets,
exact data-source namespaces, trust, timeouts, policy revisions, supported
attribute sets, allowed value sources and operation/cohort ceilings. The
administrator UI only selects within these ceilings. It cannot edit an endpoint,
secret, trust decision or source rule. Increment `PolicyRevision` when the
agreed source policy changes. Do not replace an existing `.env` or its existing
legacy settings with this example.

This syntactically complete example configures one **disabled** Profile query
source and its separate output schema. `example` is the same sourceId in
`QuerySources` and `Sources`; `example.provider` and `example.test` are generic
examples. Replace the secret through protected deployment configuration before
enabling either flag. The environment must actually be passed to the IdP
process (the deployment Compose `env_file` does this).

```dotenv
ProviderProfile__Enabled=false
ProviderProfile__MaximumAge=00:05:00
ProviderProfile__Sources__example__ProviderNamespace=example.provider
ProviderProfile__Sources__example__AllowedProperties__exampleFlag=Boolean
ProviderProfile__QuerySources__example__Enabled=false
ProviderProfile__QuerySources__example__Trusted=false
ProviderProfile__QuerySources__example__Paused=false
ProviderProfile__QuerySources__example__ProviderNamespace=example.provider
ProviderProfile__QuerySources__example__PolicyRevision=example-1
ProviderProfile__QuerySources__example__Endpoint=https://example.test/api/neutral/profile
ProviderProfile__QuerySources__example__SharedSecret=replace-with-protected-service-secret
ProviderProfile__QuerySources__example__Timeout=00:00:05
ProviderProfile__QuerySources__example__AllowPrivateNetworkHttp=false
ProviderProfile__QuerySources__example__AcceptedAttributeSets__0__0=employeeId
ProviderProfile__QuerySources__example__AcceptedAttributeSets__1__0=employeeId
ProviderProfile__QuerySources__example__AcceptedAttributeSets__1__1=accountName
ProviderProfile__QuerySources__example__AllowedValueSources__employeeId__0=person.employeeId
ProviderProfile__QuerySources__example__AllowedValueSources__employeeId__1=user.employeeId
ProviderProfile__QuerySources__example__AllowedValueSources__accountName__0=user.userName
ProviderProfile__QuerySources__example__TrustedValueProviderNamespaces__0=example.provider
```

`Sources.example.AllowedProperties` remains the approved returned-property
schema (`String`, `Boolean`, `StringArray`), separate from query attributes.
Use only output properties the producer supports. A neutral-only Profile source
can omit the legacy `Sources.example.Endpoint` and `SharedSecret` when its
matching enabled/trusted QuerySources transport is configured. If legacy will
also be used, preserve its complete transport configuration. Other capabilities
use the same `QuerySources__example__...` keys under their table prefix, with
their own endpoint and source policy; they do not need Profile's `Sources`
output schema. SourceIds are local configuration keys, not provider identities.

Omit `QueryRule` when there is one agreed rule for the namespace, capability
and attribute set. If disambiguation is needed, add, for example,
`ProviderProfile__QuerySources__example__QueryRule=example-rule-1` and configure
the exact same opaque rule at the producer. The UI displays it read-only; on
the wire it is optional `query.queryRule`, never `queryKey`. There is no automatic
discovery, weaker retry, fallback to another source or automatic wire downgrade.

Only these current product value sources are selectable:

| Query attribute | Supported value source | Required approval |
| --- | --- | --- |
| `employeeId` | `person.employeeId` or `user.employeeId` | Explicit current-account approval, or existing trusted native-field provenance for the selected field and approved source namespace. |
| `accountName` | `user.userName` | Explicit current-account approval; the local username need not equal the upstream account name. |
| `nationalIdHash` | `person.nationalId` | Explicit current-account approval including hash algorithm and provenance. |

For an agreed hash-containing set, add `nationalIdHash` to that set's next array
index and allow it with
`<prefix>__QuerySources__example__AllowedValueSources__nationalIdHash__0=person.nationalId`.
Person.NationalId stores `SHA256(UTF8(raw.Trim().ToUpperInvariant()))` as uppercase
64-hex. A hash-shaped row is insufficient evidence of this algorithm or origin.
It is sent as the existing digest, never rehashed, reversed into raw IID or used
as submitted recovery evidence. Person and User employee IDs are independent;
one never substitutes for the other. `TrustedValueProviderNamespaces` explicitly
approves reuse of existing native provenance across source namespaces. It does
not make an arbitrary persisted field trusted.

The contract vocabulary also reserves `runtime.proof.*` and
`runtime.directory.*`. Current real product callers do not supply those runtime
objects, so they are hidden from availableCandidates and rejected for new
selections. Do not configure them as a working alternative to persisted fields.
No raw IID, Email, birthdate or arbitrary claim source is available.

### Administrator journey

At `/Admin/DataSources`, **Data sources and sync -> Sources and queries** owns query inputs: the approved local values used to
locate a source record. **Field mappings -> Manage field mappings** owns supported
returned native fields and their User/Person targets. Input selection or approval
does not authorize an output write or change native-field ownership. Additional
Profile properties retain their separate approved output schema and claims rules.

1. Open **Data sources and sync -> Sources and queries -> Manage query fields**. Choose one
   of the six services and **Configured fields** (`neutral-v2`), then an available
   deployed source, supported field combination and value source for each field.
   **Existing method** selects `legacy`. Additional properties supports multiple
   named sources; the other services use one selected active source each.
2. Save settings, then reload to inspect the persisted selection. Saving performs
   no source fetch or profile write. A saved selection that becomes unavailable
   stays visible and blocked; correct or remove it explicitly. Conflicting
   selections are not silently reduced to the first source. Source status explains
   configuration eligibility (enablement, trust, pause, configuration and supported
   inputs), not live connectivity. Safe reason codes expose no endpoint, secret or
   private deployment identifier; no health probe is implied. Reasons distinguish
   `deployment_disabled`, `source_untrusted`, `source_paused`,
   `invalid_configuration` and `unsupported_inputs`; `source_unavailable` is the
   safe fallback.
3. In **Account approval**, select the account and independently verify the
   current values, Person association and source semantics. Approve those exact
   values, including the separate document-hash checkbox when applicable.
   Changing values, association, mapping or account state can invalidate approval;
   reload and review again. Trusted native employee-ID provenance can avoid
   manual approval only within its deployed namespace ceiling.
4. For Native Profile, open **Manual sync -> Choose accounts** directly, without
   opening mapping editing, then use the account picker and reviewed **Preview**
   then **Apply** flow. Apply fetches again and rejects stale context. It fills
   eligible fields while preserving nonempty values, manual fixed values
   (including intentional blanks), ownership, pause and shared-Person protections.
   Neutral Native Profile needs an existing local account and Person with trusted
   query fields; it requires no Proof login, SSO UUID or authentication binding.
   Saved source and mapped output-property selections govern a fresh preview/apply;
   changing configuration invalidates earlier review/approval contexts. Successful
   apply changes account/native state and may require explicit renewed account/source
   approval before another query. Neither action automatically establishes trust.

The page requires local application-cookie IdP Admin plus `settings.read`.
Global query GET/PUT, Native mappings GET/PUT and source pause/fallback PUT require
`DataSourcesAdministration` (local Admin or separately provisioned administrative
M2M) in addition to `settings.read`/`settings.update` as applicable; live M2M
permission ceilings and cookie CSRF protection remain. Native `GET sources`
retains `settings.read` for shared per-record dialogs. Per-record User/Person
routes are unchanged. Account approval retains its existing boundary and requires
`users.read` for reads and `users.update` for writes. Native Profile keeps its
existing User/Person write permissions. Query configuration uses the existing
Settings and field-authority schema; this round adds no HybridIdP EF migration
or manual HybridIdP SQL step, and the page relocation adds no environment keys.
General Settings retains branding, system, logging, Turnstile and mail settings.
Source selection uses public capabilities and deployment configuration without
provider-name or namespace heuristics. AuthProxy is an optional integration;
its v2 Password Sync has the separate
[producer receipt prerequisite](integrations/authproxy.md#authproxy-v2-password-sync-receipt-prerequisite).
Retain normal deployment upgrade procedures for any already outstanding schema
upgrades.

### Direct AD source for manual profile synchronization

The optional direct **Organization directory (AD/LDAP)** source is read-only and
admin-manual-only. It uses the existing internal `neutral-v2` query-selection
shape without an HTTP endpoint, shared key or legacy fallback. The protected
`QuerySourceDto.sourceType` is `directory`; existing API sources default to `http`.
No additional migration is needed for this source; retain outstanding existing
schema upgrade procedures. No previous Proof/SSO login or authentication binding
is required for the approved local query input.

Configure these exact ASP.NET environment keys privately. Empty values below
are placeholders; the example remains disabled and untrusted:

```dotenv
DirectoryNativeProfile__Enabled=false
DirectoryNativeProfile__Trusted=false
DirectoryNativeProfile__Paused=false
DirectoryNativeProfile__SourceId=
DirectoryNativeProfile__ProviderNamespace=
DirectoryNativeProfile__PolicyRevision=
DirectoryNativeProfile__SamAccountNameQueryAttribute=accountName
DirectoryNativeProfile__AllowedValueSources__0=user.userName
DirectoryIntegration__Enabled=false
DirectoryIntegration__AuthenticationEnabled=false
DirectoryIntegration__Transport=Ldaps
DirectoryLookup__Timeout=00:00:05
DirectoryCredentialTransport__Host=
DirectoryCredentialTransport__Port=636
DirectoryCredentialTransport__BaseDistinguishedName=
DirectoryCredentialTransport__ManagedSearchFilter=
DirectoryCredentialTransport__ServiceAccountDistinguishedName=
DirectoryCredentialTransport__ServiceAccountSecret=
DirectoryCredentialTransport__Timeout=00:00:05
```

Set a unique local `SourceId`, exact independently approved data `ProviderNamespace`
and nonempty `PolicyRevision`. A sourceId collision with a Native Profile HTTP
query source fails closed. After validating configuration, explicitly enable and
trust `DirectoryNativeProfile` and enable `DirectoryIntegration` for data access.
`AuthenticationEnabled` remains independently default-off. Existing Stage 1 login,
objectGUID binding, credential and native-write ceilings keep their separate rules.

For a pure data-only deployment to omit Proof transport at startup, explicitly set
`DirectoryNativeProfile__Enabled=true` and `DirectoryNativeProfile__Trusted=true`,
keep `ProviderProof__Enabled=false` with both `ProviderProof__Endpoint` and
`ProviderProof__SharedSecret` absent or blank, and keep
`DirectoryIntegration__AuthenticationEnabled=false`,
`DirectoryIntegration__TemporaryCredentialCapabilityEnabled=false`,
`NativeFieldPolicy__Stage1Enabled=false` and `CredentialMigration__Enabled=false`.
All these flags default to false; review inherited environment/configuration
overrides explicitly. `DirectoryIntegration__Enabled=true` is still required for
data access. This is a narrow startup-validator exception; other validation remains
in force. Existing configured Proof, login or staged features must retain their
real transport, credentials and validation: do not clear their settings or disable
those features merely to add this data source.

Choose exactly one deployment-agreed lookup attribute:

| SamAccountNameQueryAttribute | AllowedValueSources entries | Meaning |
| --- | --- | --- |
| `accountName` | `user.userName` | Approved local username means exact sAMAccountName in this source. |
| `employeeId` | `person.employeeId`, `user.employeeId`, or both | Only by explicit deployment agreement, the chosen stored field means exact sAMAccountName. |

For the second form, replace `SamAccountNameQueryAttribute` with `employeeId` and
set indexed `AllowedValueSources__0` and optionally `__1`. The UI selects one
approved value source; User and Person values never substitute for each other.
Neither form searches LDAP employeeID. Returned `employeeId` comes only from
actual AD `employeeID`. No domain stripping, case conversion, attribute alias,
inferred name, runtime candidate, national ID or weaker retry is added. Existing
neutral-query outer-whitespace normalization still applies.

Keep connection values and credentials outside Git. Use a service identity
restricted to reads and the independently approved managed scope. The deployment
`ManagedSearchFilter` is a filter clause combined with the fixed escaped exact
sAMAccountName predicate below `BaseDistinguishedName`; no UI LDAP editor exists.
The reader requires exactly one eligible, enabled, unlocked result. Missing,
ambiguous or unavailable data never clears local fields.

Use `Ldaps`, `StartTls` or `WindowsNegotiate`; anonymous and unprotected simple
bind are rejected. When Port is unset, LDAPS defaults to 636 and the other two
to 389; explicitly choose a matching port and retain TLS certificate validation.
LDAPS/StartTLS require `ServiceAccountDistinguishedName` and `ServiceAccountSecret`.
WindowsNegotiate accepts separate `ServiceAccountUserName`, `WindowsDomain` and
`ServiceAccountSecret`; omitting the username uses the process network identity.
Do not assume that identity is usable in a container. Both timeouts must be
positive; `DirectoryLookup__Timeout` must be at most 30 seconds.

The five production Compose modes (`internal`, `nginx`, `splithost`,
`splithost-nginx`, `splithost-nginx-nodb`) pass the selected private env file
through `idp-service.env_file`; explicit `environment` entries take precedence.
Host `dotnet run` does not automatically load the repository `.env`: supply process
environment or local user-secrets. Root `docker-compose.dev.yml` uses an explicit
environment whitelist and does not pass `DirectoryNativeProfile__*` automatically.
For a development container, use a private override with an `idp-service.environment`
mapping for each required `DirectoryNativeProfile__*` key, including indexed value
sources. Explicitly include the base and override files and the private interpolation
env file when starting Compose. Set the existing uppercase directory connection
variables consumed by the development Compose file too. Adding keys to `.env` or
using `--env-file` alone does not extend that container whitelist.

In **Sources and queries > Manage query fields**, choose **Profile sync**,
**Configured fields**, the deployed directory source, its supported input and
approved stored value source. **Existing method** is unavailable for directory
sources. Save and approve the exact account/input/Person context. In **Field
mappings**, select the source and supported returned property. Native targets
remain Nickname, FirstName, LastName, Department, JobTitle and EmployeeId; returned
choices remain `displayName`, `givenName`, `surname`, `department`, `title` and
`employeeId`. Explicit `FirstName -> displayName` uses the whole returned value
without enabling the separate Proof fallback.

**Save mappings** only saves configuration. **Save and open manual sync** saves
then opens account selection; neither action reads AD or schedules a next-login
AD sync. Select existing accounts, **Preview differences**, then explicitly
**Apply** eligible changes. Preview and apply each read AD afresh. Populated
values, manually fixed blanks, independent User/Person ownership and Email MFA
protections remain enforced. Directory Email is contact data and cannot confer
EmailConfirmed. Source/property/approval/account/security or directory policy,
connection/scope changes invalidate affected previews. A changed resolved directory
object invalidates apply too. Stale or failed apply needs a fresh preview, without
automatic retry, source switching or legacy fallback.

### Capability limits and producer agreement

A query match never links accounts, renames users, changes UserId/PersonId,
authenticates, grants recovery/reset authority or replaces local eligibility.
Profile retains property/claim approvals and cache freshness. Metadata supplies
contact evidence under its independent trust rules; it cannot retarget a
verified recovery address. RIV still checks separately submitted evidence and
requires the existing recovery OTP and reset authorization.

Lifecycle still requires explicit `ProviderLifecycle.RequiredAccounts` entries
(local account ID, `BindingKind=NeutralQuery`, exact namespace, source authority
and mapping version) and its existing freshness/eligibility policy. A producer
without an authoritative Lifecycle source must return Unavailable.
Password Sync additionally retains `CompletedDirectoryRecoveryEnabled`,
`CompletedDirectoryRequiredChangeEnabled` and `Stage2MigrationEnabled` cohort
ceilings. Enable only already authorized source operations; this does not add a
local-password write cohort. Completed source operation checks, single-send
reservation and durable Unknown/CommitUnknown barriers still apply. Never resend
an uncertain write or convert missing/ambiguous inputs into a different target.

An independent producer must implement the agreed exact attribute sets and
correlation rules. Supported sets, normalization, unique matching and operation
authority must be agreed independently; HybridIdP does not read producer storage.
The [optional AuthProxy recipe](integrations/authproxy.md) describes one adapter's
configuration and its separate Password Sync receipt prerequisite.

Unit/fixture and in-process HTTP checks establish bounded product behavior.
They do not establish connected SQL, deployed TLS, real upstream authority or
completed joint E2E. Verify those only in a separately authorized environment.

## Optional administrator native-profile synchronization (legacy mode)

[Provider Native Profile 1.0](PROVIDER_NATIVE_PROFILE_CONTRACT.md) is disabled by
default and has no new schema migration. Existing native-field authority schema
must already be present. In private deployment configuration, set
`ProviderNativeProfile__Enabled`, `TrustedProviderNamespace`, the complete
`Endpoint`, and `SharedSecret` under that section. `Timeout` defaults to five
seconds; HTTPS verifies the server certificate and redirects/cookies are disabled.
In legacy mode, the existing Proof native-field source must also be enabled, trusted, unpaused
and use the exact same namespace. No endpoint or source is inferred.

`ProviderNativeProfile__TrustedEmailProviderNamespaces__0` is an independent,
exact local confirmation allowlist and defaults to absent. Producer email
assurance alone cannot confirm Email. Preserve existing `.env`, identity links,
factors and recovery settings. This setting does not enable automatic sync:
authorized administrators preview and apply each account through the UI. The
full contract documents fill-only behavior, shared-Person ownership and stale
preview rejection. Local fixture tests do not establish a production source's
identity authority, TLS interoperability or deployment acceptance.

This guide covers the deployment of HybridIdP using Docker Compose. For a new deployment, the **interactive setup wizard** generates configuration, security secrets, and certificates. For an existing deployment, preserve the operator-managed `.env`; do not rerun the wizard to upgrade it. See [Upgrading Existing Legacy API Accounts](#upgrading-existing-legacy-api-accounts).

Release workflow actions and official build/production Compose images are pinned
to reviewed commits and digests. `.github/dependabot.yml` proposes weekly GitHub
Actions, Dockerfile and Docker Compose updates against `dev`. Review upstream
changes, platform support and local build/Compose validation before merging;
dependency updates do not publish an IdP image. The operator-selected `IDP_IMAGE`
still follows the separately approved release tag/digest policy.

Signing and encryption PFX files must be configured and readable outside
Development/Test. Missing configuration or files stops startup. Explicit
operator-generated self-signed PFX files are supported; an internal CA is not
required by this startup check.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [Quick Start (Recommended)](#quick-start-interactive-wizard)
3. [Deployment Modes](#deployment-modes)
4. [Production Configuration Contract](#production-configuration-contract)
   - [Optional Provider Lifecycle Status](#optional-provider-lifecycle-status)
   - [Upgrading Existing Legacy API Accounts](#upgrading-existing-legacy-api-accounts)
5. [One-Time Operational First Administrator](#one-time-operational-first-administrator)
6. [Verification](#verification)
7. [Advanced / Manual Configuration](#advanced--manual-configuration)
   - [Manual .env Setup](#manual-env-setup)
   - [Manual Certificate Generation](#manual-certificate-generation)
   - [Database & Redis Options](#database--redis-options)

---

## Prerequisites
- **Docker** & **Docker Compose** (v2+)
- **OpenSSL** (optional, for generating certificates via wizard)

---

## Quick Start (Interactive Wizard)

We provide an interactive script for new deployments to generate `.env`, secure passwords, and OpenIddict certificates. Both wizards leave Provider Proof and Profile disabled and set `DatabaseMigration__ApplyOnStartup=false`. Configure approved integrations separately; the wizard does not convert historical account links.

### 1. Run the Setup Script

**Windows (PowerShell):**
```powershell
cd deployment
.\setup-env.ps1
```

**Linux/macOS (Bash):**
```bash
cd deployment
chmod +x setup-env.sh
./setup-env.sh
```

### 2. Follow the Prompts
The wizard will ask for:
1.  **Deployment Mode**: Choose the architecture that fits your infrastructure (see [Deployment Modes](#deployment-modes)).
2.  **Database**: Choose SQL Server or PostgreSQL (Internal Docker or External connection).
3.  **Security**: It will auto-generate strong passwords for DBs and Certs.
4.  **Certificates**: It can generate self-signed certificates using OpenSSL automatically.
5.  **Proxy trust**: When proxy mode is selected, supply the actual proxy IPs or narrow CIDRs. No private-network or Docker-subnet trust is filled in automatically.

#### New External Database TLS

For a new external SQL Server configuration, the wizard emits `Encrypt=True` and `TrustServerCertificate=False`, so the server certificate and name must validate against the container trust store. For a new external PostgreSQL configuration, choose one verification source explicitly: use the container system trust store only when it already trusts the database CA, or place a CA file with a simple `.crt` or `.pem` filename in `deployment/certs` before running the wizard. The latter is emitted as `Ssl Mode=VerifyFull;Root Certificate=/app/certs/<filename>`; do not enter a Windows host path or a path outside that mounted directory.

Invalid external TLS input stops the wizard before it writes a new `.env`. These requirements apply only to newly generated external connection strings. Internal Docker database connection strings retain their existing networking and certificate compatibility. If an existing `.env` is detected, answer anything other than `y` to leave it byte-for-byte unchanged; answering `y` creates a timestamped backup before replacement. Existing operator-managed connection strings are not parsed, normalized, or retroactively rejected by the wizard.

### 3. Start the Application
Use the wizard's selected Compose mode for infrastructure, migration and startup.
For a new default Nginx/SQL Server installation, the sequence is below; replace
the mode and database service if you selected another topology/provider. All
commands run from `deployment/`. `IDP_IMAGE` is an approved pinned image exported
only for these commands; this does not regenerate `.env`.

```bash
export IDP_IMAGE='ghcr.io/<owner>/hybrididp-idp-service@sha256:<approved-64-hex-digest>'
compose_file='docker-compose.nginx.yml'
# Use the exact PUBLIC_AUTHORITY configured in .env, including any port.
public_authority='idp.example.com'
docker compose -f "$compose_file" -f docker-compose.ghcr-image.yml --env-file .env \
  up -d mssql-service redis-service

# After the database accepts connections and a restorable backup is confirmed:
bash ./migrate-db.sh --confirm-backup --source ghcr --image "$IDP_IMAGE" \
  --compose "$compose_file" --env-file .env

# Start the full stack, including nginx-service; do not select only idp-service.
docker compose -f "$compose_file" -f docker-compose.ghcr-image.yml --env-file .env \
  up -d --no-build
docker compose -f "$compose_file" -f docker-compose.ghcr-image.yml --env-file .env \
  exec -T nginx-service wget -q -O - --header "Host: $public_authority" http://idp-service/health/ready
```

For a selected internal PostgreSQL database, use `postgres-service redis-service`
in the infrastructure command. For the external-database mode, start only
`redis-service` and verify the external database is reachable. Repeat the
readiness check until successful, then verify JSON discovery at the configured
public issuer's `/.well-known/openid-configuration` using its trusted HTTPS
certificate before signing in.

Readiness must match the topology: Nginx mode uses `nginx-service` as above;
split-host local-Nginx modes use `nginx-gateway` in the same command. Internal
direct mode uses `curl --fail --header "Host: $public_authority" http://localhost:8080/health/ready`; split-host
direct mode uses its configured `INTERNAL_IP:8080`. Nginx modes do not expose
the IdP's port 8080 and block public `/health/ready`; query it through the
frontend container on the shared Docker network. The update helper below is
retained for existing split-host local-Nginx deployments, not this full-stack
first-start procedure.

Every internal readiness request must send the configured `PUBLIC_AUTHORITY`
as its Host header, including an optional port and without a URL scheme.
The container name or internal IP selects the connection destination; it is
not an allowed public Host. `deploy-idp.sh` reads this value from the selected
environment file as data and includes it in both gateway and direct probes.

---

## Deployment Modes

| Mode | Compose file | Description | Components |
|------|--------------|-------------|------------|
| **A. Nginx Reverse Proxy** (Recommended) | `docker-compose.nginx.yml` | Fully compliant setup with SSL termination. | `nginx` + `idp` + `db` + `redis` |
| **B. Internal / Load Balancer** | `docker-compose.internal.yml` | For when an existing LB handles SSL. | `idp` + `db` + `redis` |
| **C. Split-Host Direct** | `docker-compose.splithost.yml` | Reverse proxy is on Host A; IdP, DB, and Redis are on Host B. | `idp` + `db` + `redis` |
| **D. Split-Host Local Nginx** | `docker-compose.splithost-nginx.yml` | Gateway and IdP are isolated on Host B. | `nginx-gateway` + `idp` + `db` + `redis` |
| **E. Split-Host Local Nginx + External DB** | `docker-compose.splithost-nginx-nodb.yml` | Gateway and IdP are on Host B; the database is external. | `nginx-gateway` + `idp` + `redis` |

### Split-Host Security
For the wizard's split-host local Nginx modes (D and E), it will ask for:
- **Internal IP**: The IP of the host machine to bind the gateway to (e.g., `192.168.1.20`). This prevents exposure on public interfaces.
- **Proxy Host IP**: The IP of your external Reverse Proxy (Host A) to trust for forwarding headers.
- **Local gateway IPs/CIDRs**: The local Nginx gateway addresses as seen by the IdP. Both Host A and the gateway are required; blank trust input stops generation before a new `.env` is written.

Mode C is configured manually using `PROXY_HOST_IP`. Nginx mode A and proxy-enabled internal mode B require an explicit semicolon-separated trust list in the wizard. Proxy-disabled mode B emits an empty list.

### IP Allowlist Configuration (Split-Host)
For Split-Host deployments, you need to configure the Nginx IP allowlist:

1. **Copy the example file**:
   ```bash
   cd deployment/nginx
   cp ip-allowlist.conf.example ip-allowlist.conf
   ```

2. **Edit `ip-allowlist.conf`** to allow your reverse proxy's IP:
   ```nginx
   allow 192.168.1.10;    # Your Host A IP
   deny all;
   ```

> [!NOTE]
> The `ip-allowlist.conf` file is gitignored, so your customizations won't be overwritten by updates.

> [!TIP]
> For a deep dive into hardening the Split-Host architecture (DMZ v.s. Trusted Zone), read the [Split-Host Security Guide](../deployment/SPLIT_HOST_SECURITY.md).

---

## Production Configuration Contract

### Secret files and internal readiness

The setup wizards write generated passwords only to `.env`. The Bash wizard uses
owner-only secret files (0600) and certificate directories (0700). The Windows
wizard restricts `.env`, overwrite backups, and certificate files to the current
Windows owner, including existing files. Run the wizard under the deployment
account. If the service runs under a different identity, grant that exact service
identity read access explicitly; do not restore access for Everyone or Users.

`/health` is a shallow liveness endpoint and performs no dependency queries.
Dependency checks are at `/health/ready`, restricted to loopback or configured
trusted proxy addresses, with at most two concurrent checks. Public Nginx routes
reject this path. Use the internal container/gateway path for readiness checks.

Both split-host Nginx compose modes forward two hops. Set `Proxy__KnownProxies`
to the actual Host A address and the local gateway address/subnet in `.env`;
the compose modes require this setting and use `Proxy__ForwardLimit=2`. Keep the
trusted set limited to those proxies so rate limiting sees the originating
client address. Verify the address chain in the deployed network before release.

The `.env.example` trust list is empty. All three Nginx compose modes require a
non-empty operator list and retain its IP/CIDR entries; they supply no blanket
RFC1918 defaults. In direct split-host mode, `PROXY_HOST_IP` supplies the effective
`Proxy__KnownProxies` value, overriding that env_file key. Internal mode passes
the env_file trust list through when `Proxy__Enabled=true`. An empty list adds
no remote proxy trust; the framework's loopback defaults remain. These templates
do not rewrite an existing deployment's `.env` or prove its actual network chain.

Use the setup scripts to create the operator-managed values in `deployment/.env`, or start from `.env.example` and supply the values through an approved secret-management process. Production compose does not provide a database-password fallback.

All modes require non-empty `DATABASE_PROVIDER`, `ConnectionStrings__SqlServerConnection`, `ConnectionStrings__PostgreSqlConnection`, `ENCRYPTION_CERT_PASSWORD`, `SIGNING_CERT_PASSWORD`, `OpenIddict__Issuer`, and `PUBLIC_AUTHORITY`. `DATABASE_PROVIDER` selects the provider used by the IdP, but both database connection-string variables are required by the compose contract because both are passed into the container.

`OpenIddict__Issuer` is the stable browser-visible HTTPS origin, with no path, query, fragment, or user information. `PUBLIC_AUTHORITY` is that URI's host and optional non-default port without a scheme. They must describe the same origin, for example:

```text
OpenIddict__Issuer='https://idp.example.com/'
PUBLIC_AUTHORITY='idp.example.com'
```

Production startup derives its Host allowlist from the issuer and fails closed if the issuer is missing or invalid, or if a supplied `PUBLIC_AUTHORITY` does not match. The repository Nginx modes use `PUBLIC_AUTHORITY` as a fixed upstream Host. For direct and external-proxy modes, configure the outer trusted proxy to overwrite the upstream `Host` with this authority and to supply the effective HTTPS scheme; do not relay an arbitrary client-supplied Host.

| Compose file | Additional required non-empty values |
|--------------|--------------------------------------|
| `docker-compose.internal.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD` |
| `docker-compose.nginx.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `Proxy__KnownProxies` |
| `docker-compose.splithost.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `INTERNAL_IP`, `PROXY_HOST_IP` |
| `docker-compose.splithost-nginx.yml` | `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `INTERNAL_IP`, `Proxy__KnownProxies` |
| `docker-compose.splithost-nginx-nodb.yml` | `INTERNAL_IP`, `Proxy__KnownProxies` |

The external-database mode sets its Redis connection to the local Redis service, so it does not require `ConnectionStrings__RedisConnection`, `MSSQL_SA_PASSWORD`, or `POSTGRES_PASSWORD`. The other four modes contain both database services; therefore their SQL Server and PostgreSQL initialization passwords are required even when only one provider is selected.

Absent or empty required values fail compose validation before `deploy-idp.sh` can pull, build, or start a service. Diagnostics identify the missing variable name; they must never reveal its supplied value. Correct the named input in the operator-managed environment file and rerun the command.

For an existing deployment, add the two public-origin values to the host-side `.env` before pulling or recreating the IdP service. Use the exact public URL already consumed by OIDC clients; changing it changes the token issuer and can break relying-party validation. This configuration update does not change the database schema and does not require a database reset, volume replacement, or credential change.

### Shared Data Protection Keys at Startup

Normal application startup coordinates the configured filesystem key repository
before constructing application services. It holds an exclusive handle to
`.hybrididp-keyring.lock` while the actual application Data Protection provider
performs a protect/unprotect check. On an empty repository, ASP.NET Core creates
the initial key before another node can build its cached key ring. No separate
initialization command or new environment setting is required.

All participating nodes must use the same physical `DataProtection:KeysDirectory`
(environment variable `DataProtection__KeysDirectory`, default `/app/keys`) and
`Branding:AppName` (`Branding__AppName`, default `HybridIdP`). The storage must
provide cross-node exclusive file-handle locking and coherent file visibility;
verify these semantics for the mounted filesystem before using multiple hosts.
Process-local directories, asynchronous file replication and filesystems/mounts
that ignore locks do not meet this requirement. Do not disable .NET file locking
(`DOTNET_SYSTEM_IO_DISABLEFILELOCKING` / `System.IO.DisableFileLocking`). Each
application identity needs read/write access to the key directory and lock file;
restrict access to the same trusted identities that can already read the keys.

Lock acquisition retries contention/I/O failures for at most 30 seconds, then
startup exits unsuccessfully before serving requests. Directory/access failures
or an unusable Data Protection provider also fail startup. No process-local key
fallback is used for a configured shared repository. Investigate storage access
and other starting nodes before retrying; do not delete keys or the lock file.
The empty lock file remains on disk; the OS releases ownership when its handle
closes or its process exits, so the file's existence is not a stale lock.

Existing keys are retained, and ASP.NET Core continues to select, generate and
rotate keys with its normal lifetimes and propagation/refresh behavior. Preserve
the repository and its protection-at-rest configuration during upgrades. The
schema-only `--migrate-only` path does not acquire this lock or initialize keys.
Development retains its existing framework-default behavior when the configured
key directory does not exist. This startup coordination addresses the empty-key
cold-start gap. Session state confirmation now has bounded failure handling;
actual shared-store outage/recovery still needs deployment verification. SignalR
scale-out and background-job coordination remain separate work before full
multi-instance qualification.

ASP.NET session load/write confirmation and Email MFA cooldown cache operations
have a five-second caller IO wait bound, with no new environment variable. Keep
the existing `Redis__Enabled`, `Redis__ConnectionString` and `Redis__InstanceName`
consistent across nodes. This does not change session/cookie/OTP/token lifetimes
or add automatic memory failover. An affected state-dependent request returns
503 with localized retry guidance; unrelated DB token and cookie validation
paths do not gain a Redis preflight. A timeout does not prove the remote write
was rolled back. After restoration, start from a confirmed healthy ceremony and
repeat normal checks; see DEVELOPMENT_GUIDE.md section 10.4.3. Do not change keys,
credentials or security lifetimes to diagnose an outage.

### Data-Service Network Exposure

By default, MSSQL, PostgreSQL, and Redis are available only on their Docker networks: no database or Redis port is published on the host. Named `mssql-data`, `postgres-data`, `redis-data`, and (where used) `dataprotection-keys` volumes persist independently of this host-port choice.

For temporary local diagnostics on a mode that includes the internal MSSQL, PostgreSQL, and Redis services, explicitly include the local override after the selected compose file:

```bash
cd deployment
docker compose -f docker-compose.nginx.yml -f docker-compose.local-ports.yml --env-file .env up -d
```

`docker-compose.local-ports.yml` exposes the existing `mssql-service` (`1433`), `postgres-service` (`5432`), and `redis-service` (`6379`) on loopback only (`127.0.0.1`). It is an opt-in override, not a production default, and is not applicable to `docker-compose.splithost-nginx-nodb.yml`, which has no internal database services. Remove the override from the compose command to return to the default private posture. Do not delete or recreate volumes when adding or removing this override.

### Image and Local-Source Deployments

For GHCR, `deployment/docker-compose.ghcr-image.yml` requires `IDP_IMAGE` and sets `pull_policy: always`. Use the existing deployment flow with the exact approved release image, pinned by release tag or digest:

```bash
cd deployment
release_image='ghcr.io/<owner>/hybrididp-idp-service@sha256:<approved-64-hex-digest>'
./deploy-idp.sh --source ghcr --image "$release_image" --env-file .env
```

After validation, the script pulls the selected `idp-service` image and runs `up -d --no-build`. It reconciles both `idp-service` and `nginx-gateway` when the selected Compose mode contains the gateway, so hosts created with an older gateway mount or environment contract are safely recreated when needed; an unchanged gateway is left running by Compose. Update the host by rerunning this flow; do not reset the database or remove volumes to update an image. For local source, the same script retains its local build behavior: `--source local` runs `up -d --build`, while `--no-cache` performs the local no-cache build before startup.

The development example applies EF Core migrations during startup before normal seed processing. Production defaults `DatabaseMigration__ApplyOnStartup=false`; use the controlled schema-migration sequence below before deploying an image that contains schema changes.

### Operator-Controlled Schema Migration

The same procedure applies to SQL Server and PostgreSQL. Keep
`DatabaseMigration__ApplyOnStartup=false` in the existing `.env`. Select the exact
same Compose mode used by the deployment; do not rely on the helper's default
`docker-compose.splithost-nginx-nodb.yml` for a different topology/network. A
floating reference such as `main` or `latest` is not accepted by `migrate-db.sh`.

1. Take and verify a restorable operator-managed database backup for the selected provider.
2. Set the approved pinned image and selected Compose mode explicitly. Exporting
   `IDP_IMAGE` selects it for Compose without replacing the existing `.env`:

   ```bash
   cd deployment
   release_image='ghcr.io/<owner>/hybrididp-idp-service@sha256:<approved-64-hex-digest>'
   export IDP_IMAGE="$release_image"
   compose_file='docker-compose.splithost-nginx-nodb.yml' # Replace with your actual selected mode.
   bash ./migrate-db.sh --confirm-backup --source ghcr --image "$release_image" \
     --compose "$compose_file" --env-file .env
   ```

3. Verify that the command exits successfully. `--migrate-only` performs schema migration only: it does not seed data, start an HTTP listener, or run AD, provider, or credential-migration workflows.

   For schema-only maintenance, stop here after successful completion. Steps 4
   and 5 belong to a later application deployment, not the migration command.

   Invoke the Bash script with `bash ./migrate-db.sh ...` or `./migrate-db.sh ...`,
   rather than sourcing it into the current shell. It uses `set -euo pipefail`;
   a failed migration container returns a nonzero script exit code. Its exported
   variables stay in the script process, and `--rm` removes the one-off container
   when it exits. The script does not restart the existing IdP service.

4. For a first installation, start the complete selected stack with that same
   unchanged image, including the selected frontend:

   ```bash
   docker compose -f "$compose_file" -f docker-compose.ghcr-image.yml --env-file .env \
     up -d --no-build
   ```

   Use the topology-specific readiness checks in [Start the Application](#3-start-the-application)
   and confirm public discovery before sending traffic. This Compose command does
   not wait for application readiness. Existing split-host local-Nginx updates can
   retain `deploy-idp.sh --source ghcr --image "$release_image" --compose "$compose_file" --env-file .env`,
   which waits for readiness and preserves its existing override handling. Retain
   custom local overrides/shared networks in all existing deployment commands.
5. Only after migration, deployment, and readiness succeed, enable a separately approved staged credential rollout. Stage 1 enables only `DirectoryIntegration__Enabled`; Stage 2 additionally enables `DirectoryIntegration__AuthenticationEnabled` and `CredentialMigration__Enabled` with bounded migration-window values. Leave all three flags disabled for the schema-release deployment.

When `DatabaseMigration__ApplyOnStartup=false`, a normal application startup inspects pending migrations and fails before readiness if any remain. Do not work around that failure by enabling automatic startup migration on a production host; use the schema-only procedure instead.

To roll back, roll back the application image separately. Do not automatically run EF Core `Down` migrations. Restore the database from the verified operator backup only when a schema rollback is genuinely required.

For a schema-only update from a Windows source checkout with the .NET 10 SDK,
use `deployment/update-db.ps1`. The checkout supplies the migration version;
the connection string selects the target database. Confirm a restorable backup
and database connectivity first. This path does not require Docker, `dotnet-ef`,
website issuer settings or PFX files, and does not seed data, start the website,
deploy an image or restart an existing service. Process environment overrides
are restored when the script finishes.

```powershell
# Run from the repository root.
$secret = Read-Host 'SQL Server connection string' -AsSecureString
$connection = [System.Net.NetworkCredential]::new('', $secret).Password
try {
    .\deployment\update-db.ps1 -Provider SqlServer -ConnectionString $connection
} finally {
    Remove-Variable connection, secret
}
```

The script's internal `try/finally` restores the original process environment
and working directory on success or failure. A migration failure raises an
error. The caller's `finally` in the example removes the local credential
variable references even when that error occurs. These cleanup steps do not
restore the database or undo already committed migrations. If execution fails,
inspect the failure and migration history before continuing; do not proceed to
deployment on a failed result.

---

### Optional Provider Contract Configuration

Provider Proof, Profile and Metadata do not follow HTTP redirects.
Their fixed response ceilings are 64 KiB, 64 KiB and 16 KiB respectively,
including unknown-length/chunked bodies. Oversize cannot authenticate or supply
usable refreshed metadata; metadata read failures invalidate prior evidence.
Valid in-limit `2xx` JSON retains the existing media-type/charset handling.
No response-size tuning option or retry was added. These controls do not change
the independent lifecycle, recovery-verification or password-sync transports.

Provider Proof 1.0, Provider Metadata 1.0 and Legacy Password Sync are three
independent boundaries. Their checked-in settings are disabled or incomplete;
copying the examples does not enable an integration. Read the public contracts
before supplying any endpoint or secret:

- [Provider Proof Contract 1.0](PROVIDER_PROOF_CONTRACT.md)
- [Provider Metadata Contract 1.0](PROVIDER_METADATA_CONTRACT.md)
- [Legacy Password Sync Contract](PASSWORD_SYNC_CONTRACT.md)

Use fixed HTTPS endpoints and provision each `SharedSecret` through the approved
secret-management path. Private-network HTTP requires the boundary's explicit
`AllowPrivateNetworkHttp=true`; that setting does not prove the endpoint is
private. Proof and Metadata deadlines default to five seconds; all three
boundaries accept more than zero through thirty seconds. Restart after changing
startup-bound options.

Metadata requires the new provider-specific email snapshot migration before it
is enabled. It imports no withdrawn draft affiliation rows. Existing draft rows
remain unmapped and non-authoritative; do not drop them as part of activation or
attempt to restore authority from them. A missing separate upstream affiliation
owner remains unavailable and fails closed.

Legacy Password Sync additionally requires the provider-specific
`Hidp13LegacyPasswordSyncAttempts` migration, exact unique mappings, approved
cohorts, a connected pilot and an explicit uncertain-write settlement
procedure. The migration rewrite and model checks in this candidate were
offline only; no live or production database migration was run. Never retry
`PartialSuccess`, `CommitUnknown` or any request that may have been dispatched.
These instructions do not claim external interoperability, a connected
password write or production deployment has been verified.

### Optional Proof, SMTP and recovery-hint policies

The environment template and both wizards emit the actual defaults below.
All five production compose modes pass them through their existing
`env_file: ${IDP_ENV_FILE:-.env}`; no duplicate compose override is required.
Review the operator-managed values before enabling the corresponding integration.

| Environment key | Default | Behavior |
| --- | --- | --- |
| `ProviderProof__AllowPrivateNetworkHttp` | `false` | HTTPS is required unless private-network HTTP is explicitly allowed. Endpoint, namespace and service secret must be configured before enabling Proof login. |
| `EmailSettings__SmtpRequireTls` | `false` | Requires encryption in ordinary and test-send paths when `true`; cannot be overridden by stored mail settings or the test-send DTO. |
| `EmailSettings__SmtpValidateServerCertificate` | `true` | Uses normal MailKit certificate/hostname validation. `false` explicitly accepts any server certificate when TLS is negotiated. |
| `ForgotPasswordRecovery__PrecheckHintsEnabled` | `true` | ON/omitted preserves eligible guidance and the masked destination. OFF presents uniform public precheck, GET restoration and Send guidance without eligibility or a mask. |

SMTP security is selected with the existing `SmtpEnableSsl` value:

| `SmtpEnableSsl` | `SmtpRequireTls` | Transport |
| --- | --- | --- |
| `true` | Either | Implicit TLS (`SslOnConnect`); TLS failure prevents dispatch. |
| `false` | `true` | Mandatory STARTTLS (`StartTls`); unsupported TLS fails before authentication or Send. |
| `false` | `false` | Opportunistic STARTTLS (`StartTlsWhenAvailable`); plaintext is possible when TLS is unavailable. |

Choose the relay's matching port; the port does not select the security mode.
Existing stored host/port/credentials/from/SSL settings retain their precedence,
and the test-send DTO retains its transport settings. The two new policy flags
remain global deployment controls. Localhost and SSL-off no longer imply a
certificate exception. HTTPS/TLS can use an already trusted certificate;
an internal CA is not mandatory. HTTP, optional STARTTLS and disabled certificate
validation are explicit operator risk choices.

Default-ON recovery hints intentionally retain limited account/destination
disclosure. OFF changes the public projection only, including after refresh and
explicit Send; exact timing indistinguishability is not promised. Precheck sends
no mail. Explicit Send, current server eligibility, identity proof, destination,
lifecycle/policy, OTP and browser/session/CSRF gates remain authoritative. The hint
flag enables no recovery feature and changes no registration feedback or sign-in
continuation. Local checks do not establish real SMTP delivery, provider/browser
interoperability, directory behavior or production acceptance.

### Optional forgot-password bottom notice

The recovery page has one optional notice after the phase form or sign-in action
and support content, matching the login notice style. It appears in every recovery
phase and is independent of `LoginNotices` and the existing recovery guidance.
It defaults to hidden and adds no institution-specific content.

Set these optional values in the operator-managed `.env`:

```dotenv
ForgotPasswordRecovery__BottomNotice='@Recovery.Guidance.Bottom'
ForgotPasswordRecovery__BottomNoticeType=info
```

`BottomNotice` accepts literal plain text or `@ResourceKey`. Use the existing
Resource management workflow to supply enabled rows for the current culture and
an `en-US` fallback. Missing, disabled or blank resolved text hides the notice;
all text is Razor-encoded. Resource changes appear on the next request.
`BottomNoticeType` selects `info` (default), `warning`, `success`, `error` or
`muted`; an unrecognized type uses `info` styling. Clearing `BottomNotice` hides
the notice regardless of type.

All five production compose modes pass these keys through the existing
`env_file: ${IDP_ENV_FILE:-.env}`. No compose override or wizard change is needed.
These options are startup-bound: restart after application configuration changes.
Editing a container's env file requires recreating the container to load the new
environment; Resource edits alone require neither restart nor recreation.

### Optional Provider Lifecycle Status

The [Lifecycle Status 1.0 contract](PROVIDER_LIFECYCLE_CONTRACT.md) has an
implemented consumer that defaults to `ProviderLifecycle.Enabled=false` and
makes zero lifecycle HTTP calls while disabled. Deploying it disabled is valid
when no authoritative source or approved policy exists. Existing local
ApplicationUser and Person eligibility checks remain in force. Enabling an
explicit `RequiredAccounts` cohort against a producer that can only return
`Unavailable` denies that cohort; connectivity alone is not readiness.

Any HTTP producer conforming to the contract can supply this capability.
AuthProxy is a deployment-local producer choice, not an OSS dependency or an
assigned lifecycle authority. Each provider contract independently configures
one complete fixed endpoint, credential and deadline; there is no shared
`BaseUrl`, assumed producer host/path, SDK or shared database. The same literal
URL can serve multiple contracts only with the contract's unambiguous canonical
dispatch. Lifecycle sends `X-Provider-Contract: lifecycle-status/1.0` and body
`contractType: "lifecycle-status"`; collisions must be rejected before source
work. Per-namespace/multiple lifecycle endpoint routing remains backlog.

Lifecycle requires HTTPS with normal certificate and hostname verification.
The fixed URL has no userinfo, query or fragment. It has no
`AllowPrivateNetworkHttp` option and does not inherit the older Proof, Metadata
or Password Sync transport allowances. Redirects, automatic retries, cookies
and response decompression are disabled; nonempty `Content-Encoding` is rejected.
The monotonic deadline covers send, headers and the complete bounded body read;
producer source work must fit a shorter budget. Caller cancellation propagates.

#### Source and Policy Readiness

An institution-wide unified lifecycle standard is not a prerequisite for a
separately reviewed local policy scoped to one account type and exact namespace.
That limited policy must establish the following decisions and evidence before
enablement; this guide supplies no institutional state rules or mapping values:

1. Identify accountable source and policy owners, the authoritative account
   source, supported exact namespace, and an approved unique mapping from each
   trusted binding to an immutable source account. Record exact
   `sourceAuthority` and producer-managed `mappingVersion` and how changes are
   reviewed. Credential/API-key or IP admission proves access, not source
   authority or mapping approval.
2. Have those owners define the account-state semantics for `Enabled`,
   `Disabled`, `Retired`, `Superseded` and `Unknown` within that scope, including
   what source evidence supports each state. Never infer Enabled/Retired or a
   mapping from username prefixes, successful password proof, an AD enabled
   flag, or an employment/enrollment facet alone. HR or teaching eligibility
   must not be converted into `Person.Status`, roles or groups by this API.
3. Establish the actual source `snapshotVersion` and `observedAt`, and how the
   source guarantees the asserted state over finite `effectiveFrom` /
   `effectiveUntil` bounds. A random revision, response-time observation or
   guessed interval cannot manufacture freshness. Agree on finite local maxAge,
   host UTC clock assurance, and source work within the transport budget.
4. Approve a small explicit cohort of existing local ApplicationUsers and their
   server-owned bindings, with exact authority/mapping acceptance. Record the
   read-only validation evidence, enablement decision and containment/rollback
   plan through the deployment's protected review process. Keep real tuples and
   source identifiers out of public documentation and logs.

Until these exist, retain the disabled consumer and an unavailable producer
capability. A partially supported source adapter stays deployment-private;
unsupported or unresolved scope does not broaden the HybridIdP contract or OSS
dependencies. Missing/unverified mapping or unreadable source is `Unavailable`;
multiple resolved matches are `Ambiguous`. `NotFound` requires a completed
exact authoritative lookup with no match. `Found Unknown` requires a resolved
account with valid canonical source evidence whose authoritative state cannot
be classified; it is not a substitute for missing authority, mapping or
freshness. Neither Unknown nor any failure authorizes an operation.

#### Protected Consumer Configuration

Start from the existing [disabled JSON example](examples/provider_lifecycle_disabled_config.json.example)
and the [delivered configuration reference](implementation_plans/provider-api-lifecycle-plan.md#delivered-consumer-configuration).
Use approved per-deployment values through the existing protected configuration
path; no enabled synthetic configuration is a production example.

| Option under `ProviderLifecycle` | Deployment meaning |
| --- | --- |
| `Enabled` | Default false; invalid enabled configuration fails startup validation. |
| `Endpoint` | Independent complete fixed HTTPS lifecycle URL. |
| `SharedSecret` | Nonempty header-safe secret supplied only through protected configuration. |
| `TimeoutSeconds` | Integer 1-10, default 5; complete exchange deadline. |
| `MaxAgeSeconds` | Integer 1-300, default 60; local freshness policy. |
| `RequiredAccounts` | Nonempty when enabled; unique nonzero local ApplicationUser Guid per entry. |

Each entry requires `LocalAccountId`, exact `BindingKind` (`DirectoryBinding` or
`ExternalLogin`), `ProviderNamespace`, `SourceAuthority` and `MappingVersion`.
`LocalAccountId` identifies a local ApplicationUser, not a Person or source
account. Requiredness follows that ID even when its binding is missing.
DirectoryBinding selects exactly one existing binding in the exact namespace;
ExternalLogin selects exactly one stored login using exact
`ExternalLoginProvider` with the approved namespace. `ExternalLoginProvider` is
required only for ExternalLogin and must be omitted/null for DirectoryBinding.
`StableSubject` comes only from the existing server-owned binding/login key; it
is never configured or client supplied. Missing or ambiguous bindings deny.
Namespace, provider, authority and mapping use exact ordinal comparisons.
Producer `MappingVersion` is not a local binding ID or security stamp.

Environment keys use `__`, for example `ProviderLifecycle__Enabled`,
`ProviderLifecycle__Endpoint`, `ProviderLifecycle__SharedSecret`,
`ProviderLifecycle__TimeoutSeconds` and `ProviderLifecycle__MaxAgeSeconds`.
Indexed cohort keys use `ProviderLifecycle__RequiredAccounts__0__LocalAccountId`,
with the same index for `BindingKind`, `ProviderNamespace`, `SourceAuthority`,
`MappingVersion` and, only for ExternalLogin, `ExternalLoginProvider`. These are
key names only; values must come from the approved scope, not copied identities.

The five production Compose modes above pass the selected file into
`idp-service` through `env_file: ${IDP_ENV_FILE:-.env}`; explicit Compose
`environment` entries take precedence. A shell `.env` or Compose interpolation
file alone is not application configuration outside that pass-through. Confirm
the selected env_file and effective protected configuration without printing
secrets or identity tuples. Environment array entries can override/merge lower
precedence configuration: deliberately changing a cohort requires removing
stale higher-precedence entries so the effective policy remains coherent, not
silently weakening it. Options are startup-bound; restart the process after an
approved change. An env_file edit may require container recreation to update
the container environment; a restart alone may retain old values.

#### Read-Only Validation and Rollout

Before separately approved enablement, verify the selected image contains the
consumer implementation; local binary tests do not verify a deployed image.
Preserve a disabled baseline and distinguish synthetic fixture results from
actual-source evidence. With a separately approved test cohort, validate actual
read-only Found evidence and available deny cases, exact tuple/requestId
correlation, canonical dispatch, HTTP/outcome pairs, JSON media/size limits,
`Cache-Control: no-store`, normal TLS, credential/IP admission and deadlines.
Establish operational UTC clock trust and verify authority/mapping, actual
observation freshness and finite effective intervals at the final action time,
with zero skew. Only Found Enabled AND current local User/Person eligibility
can permit a required operation. Missing, stale, future, expired, Unknown and
non-Enabled evidence deny without fallback or persistent lifecycle writes.

Use existing verification paths and retain sanitized outcomes/correlation only;
do not activate synthetic data for real accounts. Actual source Found evidence,
clock/cohort approval and deployed-image validation remain separate gates from
contract tests or a successful unavailable-response exchange. Enable only the
reviewed cohort after those gates and the explicit operator/owner decision.
See [Lifecycle Operations](MAINTENANCE_GUIDE.md#provider-lifecycle-operations)
for outage handling, recovery and the security effect of policy rollback.

## One-Time Operational First Administrator

This optional capability is disabled by default and is for a genuinely fresh deployment only. It is not a migration, reset, repair, or account-recovery mechanism. Existing deployments leave it disabled and need no migration, database reset, or marker removal. It is also distinct from the fixed privileged Development/Test fixture (`SeedData__PrivilegedTestAdminBootstrap__Enabled`), which remains test-only, and from normal post-login administrator management.

### Prepare Host-Side Configuration

Use an approved secret-management process on the deployment host. Generate 32 random bytes out of band, encode them as unpadded base64url, and use the resulting 43-character value as the raw bootstrap token. Compute its SHA-256 digest in hexadecimal and choose a short, absolute UTC expiry. Keep the raw token only in the operator's protected, in-memory or secret-manager workflow; configuration stores only the digest and expiry.

Set these host-side values for the one-time window. The committed [`.env.example`](../deployment/.env.example) intentionally contains placeholders only.

```text
OperationalAdminBootstrap__Enabled=true
OperationalAdminBootstrap__TokenSha256Digest=<SHA256_HEX_DIGEST>
OperationalAdminBootstrap__ExpiresAtUtc=<ABSOLUTE_UTC_EXPIRY>
```

Do not put the raw token, its digest, or the administrator password in source control. Do not use a command that echoes any of them, or place them in a URL, query string, shell history, process list, or application logs. The expiry must be an absolute UTC value; a missing, expired, or non-UTC expiry leaves the capability unavailable.

The endpoint requires HTTPS. If TLS terminates at a reverse proxy, configure only the actual trusted proxy IPs or CIDRs through the existing `Proxy__Enabled` and `Proxy__KnownProxies` model so forwarded HTTPS is accepted through the forwarding middleware's known-proxy/known-network trust set. Do not treat a caller source IP as authorization: it is rate-limiting defense in depth only.

### Perform the One-Time Request

From a protected operator client, make one HTTPS `POST` request to `/api/operational-bootstrap/admin` with `Content-Type: application/json` and the raw token only in this dedicated header:

```text
X-HybridAuth-Bootstrap-Token: <43_CHARACTER_BASE64URL_TOKEN>
```

The JSON body contains operator-chosen, unique values and must be handled without exposing its password:

```json
{
  "email": "<UNIQUE_ADMIN_EMAIL>",
  "name": "<UNIQUE_ADMIN_NAME>",
  "password": "<password>"
}
```

For SQL Server and PostgreSQL, the operation uses a serializable, all-or-nothing transaction. A successful request creates exactly one initial Admin account and the system-owned completion marker. A replay, a request after expiry, an existing or ambiguous identity state, invalid prerequisite Admin role, or an uncertain outcome remains closed and returns the generic unavailable result. A successful response is `201` with `operational_bootstrap_completed`; the generic unavailable response is `404` with `operational_bootstrap_unavailable`.

Never retry a request when the response is lost or uncertain. Do not attempt to make the capability reusable by deleting identities, resetting the database, removing the marker, reusing the token, or promoting another account. The marker is system-owned and cannot be changed through the ordinary settings API.

### Clean Up and Continue Operations

Immediately after a confirmed success, disable the capability and remove the three bootstrap configuration values (including digest and expiry) from host-side configuration or secret storage. Retain the database and its completion marker. Sign in with the new administrator and use the normal authenticated administrator management surface for all later user and role changes.

For a future Docker deployment, use the released image together with host-side configuration and secrets, then follow the normal image pull/recreate workflow described above. This documentation does not instruct an image pull, publish, or deployment now.

---

## Verification

1.  **Check Containers**:
    ```bash
    docker compose ps
    ```
2.  **Health Check**:
    ```bash
    curl -k http://localhost:8080/health  # Port depends on mode (80/443/8080)
    ```
    > [!IMPORTANT]
    > If you configured `INTERNAL_IP` to a specific IP (e.g., `192.168.x.x`), `curl localhost` will fail because the service is strictly bound to that IP. Use the configured IP instead: `curl -k http://192.168.x.x:8080/health`.
3.  **OpenID Discovery**:
    Navigate to `https://your-domain/.well-known/openid-configuration` and ensure it returns JSON data.

---

## Reverse Proxy & SignalR Support

If you are running HybridIdP behind an additional reverse proxy (e.g., BunkerWeb, Traefik, or another Nginx instance), ensure your proxy is configured to forward WebSocket upgrade headers correctly.

### WebSocket Configuration
The Nginx configuration included in this repository handles WebSocket upgrades via the `Connection` and `Upgrade` headers. Your external proxy must also forward these headers:

- `Upgrade`: `$http_upgrade`
- `Connection`: `Upgrade` (or derived from `$http_upgrade`)

### Sticky Sessions (Scaling)
SignalR requires sticky sessions (session affinity) when scaling the IdP service to multiple instances. Ensure your load balancer or reverse proxy routes requests from the same client to the same backend instance based on a cookie or IP hash.

> [!NOTE]
> Single-instance deployments (default) do not require sticky sessions.

---

## Advanced / Manual Configuration

If you prefer not to use the wizard, follow these steps.

### Manual .env Setup
1.  Copy `.env.example` to `.env`.
2.  Fill in the values. NOTABLE variables:
    -   `ASPNETCORE_ENVIRONMENT`: `Production`
    -   `DATABASE_PROVIDER`: `SqlServer` or `PostgreSQL`
    -   `Proxy__Enabled`: `true` if behind any proxy.
    -   `Proxy__KnownProxies`: CIDR ranges or specific IPs of your proxy.

### Manual Certificate Generation
HybridIdP requires two certificates: **Encryption** and **Signing**.

```bash
mkdir -p deployment/certs
cd deployment/certs

### Alternative: Using Step-CA
If you prefer `step-ca`:

```bash
# Encryption Cert
step ca certificate "HybridIdP Encryption" encryption.crt encryption.key --kty RSA --size 4096
step certificate p12 encryption.pfx encryption.crt encryption.key --password=YOUR_PASSWORD

# Signing Cert
step ca certificate "HybridIdP Signing" signing.crt signing.key --kty RSA --size 4096
step certificate p12 signing.pfx signing.crt signing.key --password=YOUR_PASSWORD
```

### Alternative: Using OpenSSL
```bash
# Generate Encryption Cert
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout encryption.key -out encryption.crt -subj "/CN=HybridIdP Encryption"
openssl pkcs12 -export -out encryption.pfx -inkey encryption.key -in encryption.crt -password pass:YOUR_PASSWORD

# Generate Signing Cert
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout signing.key -out signing.crt -subj "/CN=HybridIdP Signing"
openssl pkcs12 -export -out signing.pfx -inkey signing.key -in signing.crt -password pass:YOUR_PASSWORD
```

> [!IMPORTANT]
> You must update `ENCRYPTION_CERT_PASSWORD` and `SIGNING_CERT_PASSWORD` in your `.env` file to match the passwords used above. **Always wrap the values in single quotes (e.g., `'password'`) to prevent issues with special characters like `#`.**

### Database & Redis Options
-   **Redis**: Set `Redis__Enabled=false` to use In-Memory caching (not recommended for multi-instance production).
-   **External DB**: For a new SQL Server value, require `Encrypt=True;TrustServerCertificate=False`. For a new PostgreSQL value, require `Ssl Mode=VerifyFull` and either the container system trust store or `Root Certificate=/app/certs/<simple-ca-filename>`. Do not use host-specific paths in a container connection string.

### Troubleshooting Manual Setup
**"Permission denied" when reading .env:**
If you see an error like `open .env: permission denied`, you may have created the file as `root` (e.g., via `sudo`) or set strict permissions (e.g., `chmod 600`) while the file is owned by another user.
Ensure your current user owns the file:
```bash
sudo chown $(whoami) deployment/.env
```
If your **Step-CA** is running on a different machine (e.g., **Host C** with IP `192.168.1.50`) and using an internal hostname (e.g., `ca.internal`), Docker containers might fail to resolve it.

**Option A: Manual Host Mapping (Recommended for Static IPs)**
Directly map the hostname to Host C's IP.
```yaml
services:
  idp-service:
    extra_hosts:
      - "ca.internal:192.168.1.50" # Map ca.internal to Host C IP
```

**Option B: Custom Internal DNS**
If you have an Internal DNS Server (e.g., AD DC) that knows where `ca.internal` is:
```yaml
services:
  idp-service:
    dns:
      - 192.168.1.1  # Your Internal DNS Server IP
      - 8.8.8.8      # Fallback

### Connecting to External Docker Networks (Legacy Services)
If your IdP needs to connect to another service running in a different Docker Compose project (e.g., a **Legacy Service**), you can join its network without modifying the main `docker-compose.yml`.

1.  **Identify the Network Name**: Run `docker network ls` to find the network name (e.g., `legacy_backend`).
2.  **Create `deployment/docker-compose.override.yml`**:
    ```yaml
    services:
      idp-service:
        networks:
          - legacy_network

    networks:
      legacy_network:
        external: true
        name: legacy_backend  # Actual name from 'docker network ls'
    ```
    > **Note**: `docker-compose.override.yml` is git-ignored, so your local setting won't be overwritten by updates.

3.  **Start with Override**:
    You must explicitly include both files when starting:
    ```bash
    docker compose -f docker-compose.splithost-nginx.yml -f docker-compose.override.yml --env-file .env up -d
    ```
```



## Unified API login and Profile upgrade

Clients upgrading from 1.4.x to 1.6.x that display user names must request the
OIDC `profile` scope, have it allowed in the client configuration, and obtain
it in the granted scopes. Reauthorize after updating the request/permissions
to obtain new tokens. Unlike 1.4.3's unconditional `name`, 1.6.x omits `name`
and `preferred_username` without granted `profile`; a custom `employee-id`
scope does not imply `profile`. `sub` remains the local UserId, so a client
falling back to `sub` may show a UUID. This is a scope-disclosure change, not
a change to token claim nesting.

### Upgrading Existing Legacy API Accounts

The flat `authenticated` response reader and `LegacyAuth__*` settings are
retired. A 1.4.x `.env` can be retained, but unchanged LegacyAuth configuration
does not enable Proof login. Local-password accounts and completed directory
migration records retain their credential authority. Google/Microsoft browser
federation is separate from this API-login upgrade.

Before switching an existing deployment, confirm with the producer/source owner
that the configured Proof 1.0 endpoint returns the exact approved namespace and
the same immutable subject as **every** historical key selected for backfill.
The 1.4.3 reader stored `ssoUuid ?? userId`; a Legacy row count, GUID-shaped key,
account name, email or successful login is not proof that all keys share one
authority. An old fallback `userId` must not be reinterpreted as an SSO UUID.
Academic/HR profile subjects are also not substitutes for an SSO subject. If any
historical row is ambiguous, stop the bulk procedure and resolve its authority
through a separately verified account-linking process.

For an approved same-subject transition, the SQL Server-only
[`backfill-legacy-proof-links.sql`](../deployment/backfill-legacy-proof-links.sql)
adds `(approved namespace, existing ProviderKey, existing UserId)` to
`AspNetUserLogins`. It retains the original `Legacy` row and other provider
links. It does not recreate users, change Person/MFA/Passkey/recovery-code data,
enable an integration or modify `.env`. Schema migration does not run this data
backfill. PostgreSQL deployments need a separately reviewed procedure; do not
run this SQL Server script against them.

Use this order in an approved maintenance window:

1. Record the running image and Compose project/directory. Keep a protected
   backup of `.env`, certificates, local overrides/network configuration and a
   verified restorable database backup. Preserve the DataProtection volume and
   Compose project identity. Update the deployment files to the reviewed
   revision containing this script without overwriting operator files; an
   already released image does not itself install newer host-side scripts.
2. If the selected database has pending schema changes, run the
   [schema-only migration](#operator-controlled-schema-migration) using the
   approved pinned image. SQL Server deployments already at
   `20261008032457_AddProviderProfilesAndClaimConditions` need no additional
   schema migration solely for the 1.6.1 patch. This does not establish provider
   identity equivalence or backfill links.
3. Pause account creation/link-management writers for the cutover and keep them
   paused until the new image is ready. Make a protected working copy of
   `backfill-legacy-proof-links.sql` outside source control. Edit
   `@ExpectedDatabaseName` and `@TargetProviderNamespace` with the exact database and
   verified namespace, leaving `@Apply = 0`. Run it against that database using
   SSMS or `sqlcmd`. Review the reported counts. A conflict rejects the entire
   operation; do not delete or rewrite existing rows to force it through.
4. Only after the source-identity confirmation covers the complete Legacy set,
   set `@SameSubjectAuthorityConfirmed = 1`, `@BackupConfirmed = 1`,
   `@ExpectedLegacyCount` to the reviewed current count, and `@Apply = 1` in the
   protected copy. Execute it again and verify the committed counts. A changed
   source count requires a new review. Repeating an unchanged successful run
   inserts zero additional links. Keep execution reports private; do not publish
   real subjects, account identifiers or credentials.
5. Edit only the necessary keys in the original `.env`:

   ```dotenv
   ProviderProof__Enabled=true
   ProviderProof__TrustedProviderNamespace=<exact verified producer namespace>
   ProviderProof__Endpoint=<complete Proof 1.0 endpoint URI>
   ProviderProof__SharedSecret='<copy the exact approved service secret>'
   ProviderProof__AllowPrivateNetworkHttp=false
   DatabaseMigration__ApplyOnStartup=false
   ```

   The endpoint/secret can reuse the original `LegacyAuth__LoginUrl` and
   `LegacyAuth__Secret` **values** only if that same endpoint supports Proof 1.0
   with the approved identity tuple. Copy values explicitly; do not introduce
   `.env` variable references. Use Compose dotenv quoting/escaping for the exact
   secret, and do not source `.env` in a shell. Set
   `ProviderProof__AllowPrivateNetworkHttp=true` only for an explicitly approved
   HTTP endpoint on a controlled private network; HTTPS keeps it `false`.
   `TrustedProviderNamespace` must not be `Legacy`. Old `LegacyAuth__*` entries
   may remain for application rollback, but the new image ignores them.

   Email confirmation has its own default-off source policy. A valid Proof
   Email is saved for new accounts and fills missing Email on the original
   account regardless of this policy. Example only: if the operator independently
   selects and verifies the illustrative `example.provider` namespace, they may
   add this setting to the existing private `.env`. Replace the example with
   the operator's own EXACT configured provider namespace:

   ```dotenv
   ProviderProof__TrustedEmailProviderNamespaces__0=example.provider
   ```

   In the private environment, `ProviderProof__TrustedProviderNamespace` must
   use the same exact namespace, with explicit Email assurance in a valid Proof
   response. Namespace matching is exact and case-sensitive. Omit the list to
   leave new confirmation off;
   existing confirmations are not revoked. Email never links identity or
   retargets an enrolled Email OTP factor. The Compose modes pass the selected
   env file to the application through `env_file`, including indexed entries;
   direct host deployments must expose these keys as application environment
   variables. Do not regenerate the existing `.env` with the setup wizard.

   Retain database strings, issuer/public authority, certificate passwords,
   SMTP and Fido2 domain/origins. Current Nginx modes additionally require a
   nonempty `Proxy__KnownProxies` containing the actual trusted proxy/gateway
   addresses; use existing correct values and do not add broad private-network
   trust. Both database connection-string variables were already required by
   the 1.4.3 Compose contract. Profile, directory integration, lifecycle and
   credential migration can stay disabled; Proof login does not require them.
6. Deploy the approved image using the existing project directory, `.env` and
   Compose mode. For the external-SQL-Server/local-Nginx mode:

   ```bash
   cd /path/to/HybridIdP/deployment
   release_image='ghcr.io/<owner>/hybrididp-idp-service@sha256:<approved-64-hex-digest>'
   bash ./deploy-idp.sh --source ghcr --image "$release_image" \
     --compose docker-compose.splithost-nginx-nodb.yml --env-file .env
   ```

   `--image` selects the release without rewriting `IDP_IMAGE` in `.env`.
   Preserve the existing custom override/shared Docker network used to reach
   the producer. The helper reconciles IdP and the local Nginx gateway when
   present; do not use `down -v`, reset the database or replace volumes.
7. Wait for readiness, verify public OIDC discovery, then test an existing
   API-linked account through password login, existing MFA, Profile and a
   TestClient authorization/token round trip. Confirm it resolves to the same
   existing UserId with its retained factors/roles. Also verify an independent
   Google/local account if deployed. Readiness alone does not prove an upstream
   identity transition or successful end-user login.

For example, after editing the protected SQL copy, `sqlcmd -S '<sql-server>'
-d '<database>' -U '<migration-user>' -b -i '/protected/backfill.sql'` executes
it and returns a nonzero status on a SQL error. Supply credentials through the
client's protected prompt/authentication mechanism; do not put a password on
the command line or disable server-certificate validation for this operation.

Application rollback uses the recorded old image with the saved original
configuration and the same volumes/project. Retained Legacy rows allow the old
reader to retain its original links; the producer must also still support the
old request/response contract for that rollback. Additive links do not provide
wire-format compatibility. Leave additive Proof links and nullable
schema columns in place; do not automatically delete links or run EF `Down`.
A database restore is a separate, explicitly approved recovery operation and
can discard writes made after the backup.

### Native-field policy configuration and upgrade

Before activating an application build containing native-field policies, apply
`AddNativeFieldAuthority` for the selected database with the existing
[operator-controlled schema migration](#operator-controlled-schema-migration)
procedure and approved artifact. SQL Server uses
`20261020030913_AddNativeFieldAuthority`; PostgreSQL uses
`20261020030931_AddNativeFieldAuthority`. These add nullable
`NativeFieldStateJson` to `AspNetUsers` and `Persons`, plus the small
`NativeFieldSourcePolicies` pause/revision table. No historical values, ownership
or identity bindings are backfilled. Existing nonempty unattributed native values
are preserved by default. This is a schema/configuration requirement for a future
activation, not a release announcement or evidence of an applied migration.

Keep the original `.env`, endpoints, secrets, account links and existing trust
settings. Do not rerun the setup wizard. The new native settings are:

```dotenv
# Default true preserves existing explicitly enabled/trusted Proof deployments.
NativeFieldPolicy__ProofEnabled=true
# Default false; existing Stage 1 configuration alone is insufficient.
NativeFieldPolicy__Stage1Enabled=false
# Example only: replace with the exact verified provider namespace before opting in.
# NativeFieldPolicy__TrustedStage1ProviderNamespaces__0=org-directory
```

Proof native writes also require `ProviderProof__Enabled=true` and its exact
`ProviderProof__TrustedProviderNamespace`. That existing enablement defaults to
`false`; the new Proof ceiling does not activate a source or add Email trust.
Already enabled deployments do not need to add a mandatory Proof flag. Set
`NativeFieldPolicy__ProofEnabled=false` to block native writes while preserving
login and independent Profile refresh.

The Data sources and sync UI displays the Stage 1 source as **Organization directory (AD/LDAP)**,
separately from **Sign-in verification source**. For an approved directory native
rollout, explicitly set
`NativeFieldPolicy__Stage1Enabled=true` and at least one exact indexed trusted
namespace, alongside existing `DirectoryIntegration__Enabled=true` and its
validated configuration. Matching is case-sensitive, with no wildcard or inferred
namespace. Leaving either new setting at its default stops the six native-field
writes; directory binding and independent contact/Email behavior remain under
their existing rules. Stage 2 credential/authentication flags do not substitute
for these native settings.

All five production Compose modes load the existing application `env_file`,
including indexed entries; no new Compose override is needed. Recreate/restart
the application through the existing selected deployment procedure after an
approved configuration change. UI source status/pause is configuration state,
not a remote connectivity check. Pause/resume cannot create source trust.
Only the six documented non-security fields are supported; no automatic source
switching or source-driven clear is available. See
[field policies and operator workflow](IDENTITY_FIELD_MAPPING.md#native-field-policy-and-operator-workflow)
for manual fixed blanks, explicit unlock/overwrite, exact binding, independent
User/Person ownership and the next eligible normal-sync timing.

For the optional FirstName fill strategy, also apply
`AddFirstNameDisplayNameFallback`: SQL Server
`20261020091155_AddFirstNameDisplayNameFallback` or PostgreSQL
`20261020091320_AddFirstNameDisplayNameFallback`. It adds a default-false
selection and nullable exact namespace to `NativeFieldSourcePolicies`; it
does not rewrite User/Person values or assign sources to historical data.

Retain the verified `ProviderProof__Enabled`,
`ProviderProof__TrustedProviderNamespace`, endpoint and secret in the original
private `.env`, with `NativeFieldPolicy__ProofEnabled=true` to permit native
writes. These existing keys are the enablement/trust ceiling. There is no
additional environment flag for the fallback selection. All five production
Compose files pass the selected `.env` through `idp-service.env_file`.

In **Data sources and sync > Field mappings > Manage field mappings**, edit
the native/source/property mappings and optional **Use the full name to fill a
missing first name** strategy. **Save mappings** saves configuration only;
**Save and open manual sync** then opens account selection, read-only preview and explicit
apply. Editable mappings use the existing system-managed Settings storage and
source-policy revisions, with no additional migration or environment flag.
Saved rows retain their exact namespace; changing deployment trust does not
transfer mappings to a replacement source. The fallback starts off; changing
the configured namespace requires a new explicit selection. The next eligible
normal Proof login can fill a missing, unprotected FirstName from that
response's assured whole displayName only when givenName is missing. Populated
FirstName and manually fixed blanks are preserved even if overwrite is selected.
Pause, source trust, exact binding and independent User/Person ownership still
apply. Turning the strategy off does not undo previously filled values.

Username naming is selected separately in the same dialog under **New-account
username**. **Source account** is the default; **Source Email** uses a fresh
assured Proof Email only for new accounts of that exact namespace. Save needs
no new environment flag or schema migration and never renames existing accounts.
Proof authentication enablement/trust remains authoritative. Pausing native sync
does not disable or change the username choice, and missing/invalid/occupied
Email denies creation without canonical fallback. Email confirmation remains
independent: keep its existing trust list unchanged unless explicitly intended.
See [username selection](IDENTITY_FIELD_MAPPING.md#new-account-username-selection)
for source-login compatibility and separately reviewed historical rename limits.

### Optional Profile Activation

Apply the selected provider's `AddProviderProfilesAndClaimConditions` migration
through the existing deployment process. It adds nullable Person profile JSON
and nullable claim source/condition columns; no property values are backfilled.
Profile is default-off. Configure its independent endpoint, secret and approved
String/Boolean/StringArray properties before activation. A scalar remains a
scalar; declare `StringArray` only for an actual array property. The source dictionary can be supplied
with normal ASP.NET configuration (including environment keys in an env_file),
without defining source-specific properties in OSS. See the configuration example
in [Provider Profile 1.0](PROVIDER_PROFILE_CONTRACT.md) and deployment/.env.example.

Select `ProviderProfile__MaximumAge` for the deployment; the initial default is
five minutes, zero means re-confirm each applicable issuance request. An unchanged
hash is not a freshness assertion. Profile failure omits dependent claims; existing
JWTs remain valid until their ordinary expiration. Reverting the application may
leave additive nullable columns in place; do not drop Person JSON for rollback.
