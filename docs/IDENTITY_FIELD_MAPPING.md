# Identity source and field mapping

This inventory records the implemented mappings and bounded native-field
policies for Proof and Stage 1. It does not enable an
integration, change existing identifiers, rename accounts or authorize data
cleanup. Examples must use generic values such as `example.provider` and
`source-account`; deployment identifiers and account data belong in private
configuration only.

## Entity responsibilities

| Record | Responsibility | Identity boundary |
| --- | --- | --- |
| `ApplicationUser` | Local sign-in account, username, contact Email, confirmation, credentials, factors and role membership | The local `Id` remains the OIDC subject. Changing display/contact fields does not select a different identity. |
| `Person` | Shared person profile and local lifecycle state; may have multiple accounts | `PersonId` is an explicit link, never inferred from a matching name. |
| `AspNetUserLogins` | Exact external provider namespace and opaque subject linked to one local account | Neither Email nor `canonicalAccount` replaces this tuple. |
| `ProviderSubjectDirectoryBinding` | Existing staged integration's durable local account/provider subject/directory object relationship and account alias | Alias refresh does not replace the directory object or local account. |
| `Person.ProviderProfilesJson` | Source-scoped, approved extra properties and confirmation metadata | This is claims input, not account-linking, role, credential or recovery authority. |

Profile fields exist on both `ApplicationUser` and `Person`. Native-field
policy state and ownership are independent for each record and field; display
projection does not merge their authority.

## Which source and API supplies which data

| Source boundary | Input and timing | Allowed purpose |
| --- | --- | --- |
| Provider Proof 1.0 | Configured Proof endpoint; credential submission and a validated authenticated result | Establish the exact provider identity and supply an optional assured contact/display profile for the login path. |
| Provider Profile 1.0 | Independently configured source endpoint; exact established namespace/subject tuple | Return complete approved `extraProperties` for source-scoped snapshots and claims. It does not automatically populate native User/Person profile columns. |
| Administrative Claims preview | Protected preview request; current upstream Profile read for the selected existing account | Compute hypothetical payloads. No Profile snapshot, native profile, session, grant or token is written. |
| Provider Metadata 1.0 | Its independent metadata/evidence flow | Supply the documented metadata contract; it is not an alternative general person-profile synchronizer. |
| Provider Lifecycle Status 1.0 | Its separately enabled lifecycle flow | Supply lifecycle evidence under the lifecycle contract; display/profile JSON must not be interpreted as eligibility. |
| Protected directory lookup | Existing staged integration; exact-one eligible managed-directory result after the configured Proof path | Establish/check the durable directory binding and refresh the fields listed below. This is not generic browser external login. |
| Local operator edit | Protected administrative APIs under their existing permissions | Explicit local maintenance; edits to supported native fields can fix values, including intentional blanks, against later source updates. |

The endpoints, secrets and enablement flags remain separate. Selecting a
namespace does not make every API or its fields trusted for every purpose.
See [authentication integration](AUTHENTICATION_INTEGRATION.md),
[Proof](PROVIDER_PROOF_CONTRACT.md), [Profile](PROVIDER_PROFILE_CONTRACT.md),
[Metadata](PROVIDER_METADATA_CONTRACT.md) and
[Lifecycle](PROVIDER_LIFECYCLE_CONTRACT.md).

## Independent Provider Proof login: implemented mapping

The login service validates the contract, namespace, subject, required actions
and profile assurance before passing an `ExternalAuthResult` to JIT provisioning.

| Proof field | Local target | Existing-account behavior |
| --- | --- | --- |
| `providerNamespace` + `stableSubject` | `AspNetUserLogins` exact provider/key | Resolve the original account first; no Email/name-based matching in the canonical-account provisioning path. |
| `canonicalAccount` | New `ApplicationUser.UserName` | Used only at initial canonical-account creation, subject to local validation and uniqueness. Repeat login does not rename the account. Older Email-form usernames remain valid stored names. |
| `profile.email` | `ApplicationUser.Email`, `Person.Email` | With local Email trust off, fill missing values without newly confirming them. With trust on, existing verified-source update rules apply. An enrolled account Email OTP target is not replaced. |
| Explicit Email assurance + exact local Email trust entry | `ApplicationUser.EmailConfirmed` | Both are required for new confirmation. Saving Email alone does not confirm it. This does not grant recovery-address trust or enroll MFA. |
| `profile.givenName` | `ApplicationUser.FirstName`, `Person.FirstName` | Subject to the native-field policy below. |
| `profile.surname` | `ApplicationUser.LastName`, `Person.LastName` | Subject to the native-field policy below. |
| `profile.displayName` | `AspNetUserLogins.ProviderDisplayName`, `ApplicationUser.Nickname`, `Person.Nickname` | Login-binding display metadata refresh remains independent. Native Nickname follows field policy. User permits 256 characters; Person permits 100. A source value over 100 is skipped for Person without truncation or login failure. No name splitting or username change. |
| `profile.department` | `ApplicationUser.Department`, `Person.Department` | Subject to the native-field policy below. |
| `profile.title` | `ApplicationUser.JobTitle`, `Person.JobTitle` | Subject to the native-field policy below. |
| `profile.employeeId` | `ApplicationUser.EmployeeId`, `Person.EmployeeId` | Subject to field policy; not the provider subject or an automatic account-linking key. |

Every nonblank Proof profile field must have its matching `assuredFields`
entry. A valid contact Email can be stored independently of the additional
local decision to confirm it. Profile extra properties do not substitute for
a missing Proof Email in this path.

New and existing usernames can therefore have different presentation formats.
Consumers must use the documented stable subject, not assume that a username
is always an Email address. Internal display projection and issued claims also
have their own scope/mapping rules; `UserName`, `name`, `preferred_username`
and `sub` must not be treated as interchangeable fields.

For issued user claims and read-only Claims Preview, `name` uses the existing
structured First/Middle/LastName when present, then the effective native User
`Nickname`, then the existing username fallback. It never reads a raw login
binding display name to bypass native-field policy. The `profile` scope is
still required; `preferred_username` and `sub` retain their existing mappings.
Profile's `Person.FullName` uses that Person's structured name or native
`Nickname`; it does not substitute the account username or another account's
name. A custom claim mapped to `Person.EmployeeId` reads the persisted value
unchanged. If Proof omits `profile.employeeId`, neither `canonicalAccount`,
the opaque subject nor an Email-form username supplies a substitute.

Implementation: [LoginService](../Infrastructure/Services/LoginService.cs),
[JitProvisioningService](../Infrastructure/Identity/JitProvisioningService.cs)
and [ManagedCredentialDisplay](../Infrastructure/Identity/ManagedCredentialDisplay.cs).

## Existing staged directory flow: implemented mapping

The LDAP transport currently reads the following fixed attributes into a typed
directory result. A deployment must independently verify its directory's
attribute semantics and configured managed-search scope. This table does not
authorize deriving a Proof subject from a directory attribute.

| LDAP attribute | Typed directory field | Refresh target |
| --- | --- | --- |
| `objectGUID` | `ObjectId` | Durable directory binding; not a display identifier. |
| `sAMAccountName` | `CanonicalAccount` | Checked lookup/alias value; not an automatic rename of `ApplicationUser.UserName`. |
| `displayName` | `Profile.DisplayName` | `ApplicationUser.Nickname` and `Person.Nickname`. |
| `givenName` | `Profile.GivenName` | `ApplicationUser.FirstName` and `Person.FirstName`. |
| `sn` | `Profile.Surname` | `ApplicationUser.LastName` and `Person.LastName`. |
| `mail` | `Profile.Email` | User/Person contact Email; account Email is not retargeted while Email MFA is enrolled. This refresh does not set `EmailConfirmed`. |
| `department` | `Profile.Department` | User/Person `Department`. |
| `title` | `Profile.Title` | User/Person `JobTitle`. |
| `employeeID` | `Profile.EmployeeId` | User/Person `EmployeeId`; not identity matching. |
| `userAccountControl`, `lockoutTime` and the managed-directory gates | Eligibility/enabled/locked result | Sign-in and binding eligibility checks, not a general overwrite of local Person lifecycle fields. |

Directory native refresh follows the policy below, including the explicit
Stage 1 deployment ceiling and exact namespace allowlist. It updates only
an already bound local account and its linked Person. Initial local
account selection/provisioning in the staged path is distinct from independent
Proof canonical-account naming; do not assume those creation rules are equal.
Directory profile refresh does not rename the local account or assign roles.

Implementation: [LDAP transport](../Infrastructure/Directory/LdapProtectedDirectoryTransport.cs),
[protected lookup](../Infrastructure/Directory/ProtectedDirectoryIdentityLookup.cs)
and [binding refresh](../Infrastructure/Services/Stage1BindingRefreshService.cs).

## Native-field policy and operator workflow

Only `Nickname`, `FirstName`, `LastName`, `Department`, `JobTitle` and
`EmployeeId` are supported, using the existing Proof and Stage 1 mappings above.
There is no automatic source fallback or source-priority engine. Browser
providers and Provider Profile extra properties are not selectable native sources.

Deployment configuration remains authoritative:

| Option | Default | Effective native-write boundary |
| --- | --- | --- |
| `NativeFieldPolicy:ProofEnabled` | `true` | Also requires `ProviderProof:Enabled` (default `false`) and the exact configured `ProviderProof:TrustedProviderNamespace`. The compatibility default retains already explicitly enabled Proof behavior; existing deployments need no new mandatory Proof flag. Setting it to `false` stops native writes, not login or Profile refresh. |
| `NativeFieldPolicy:Stage1Enabled` | `false` | Also requires `DirectoryIntegration:Enabled` and exact membership in `NativeFieldPolicy:TrustedStage1ProviderNamespaces` (default empty). Existing directory configuration alone no longer permits these six native writes. |

Namespaces compare exactly and case-sensitively. Configure the deployment's
verified namespace, not an example or a derived account name. See the
[upgrade/configuration procedure](DEPLOYMENT_GUIDE.md#native-field-policy-configuration-and-upgrade).
UI pause/resume cannot enable or trust a deployment-disabled source. Pausing
retains configuration, bindings, values and ownership, and stops only native
synchronization. A temporary source outage preserves current native values.
Source status describes configuration and pause state, not connectivity health.

Every source write requires the exact persisted identity: Proof uses local account,
provider namespace and opaque subject in `AspNetUserLogins`; Stage 1 additionally
requires the persisted directory object in `ProviderSubjectDirectoryBinding`.
Enablement or assignment never creates that binding. User and Person field
owners are separate. A shared Person with multiple accounts requires explicit
source assignment before its first source claim; another account's later login
cannot take ownership. Assignment may select a supported source of the same
owner account, but cannot transfer ownership to another account.

Defaults preserve nonempty native values, including historical values without
source attribution. Overwrite is an explicit per-field opt-in and still requires
the binding and owner checks. Missing, null, empty and whitespace-only source
values are no-ops. Neither current source contract supplies authoritative clear
intent: source clear is disabled in the UI and rejected by the API.

An operator can explicitly save and fix a value, including an empty input to
clear it. It remains fixed until explicit Unlock. Assigning a source does not
unlock it. Unlock retains the selected owner and overwrite choice; a subsequent
write must still satisfy all policy checks. Ordinary User/Person edits protect
deliberately changed supported values or explicit empty clears; omitted/null
fields and unchanged nonempty values do not acquire protection. Use the policy
dialog to fix a value even when it has not changed.

| UI entry | Read / update permissions | Operation |
| --- | --- | --- |
| `/Admin/Settings`, Native field policies section before Branding | `settings.read` / `settings.update` | Inspect supported fields and deployment/pause status; pause or resume an enabled source. |
| `/Admin/Users`, row action menu, Native field policies | `users.read` / `users.update` | Select a User field, save and fix a value, Unlock, or assign an exact existing binding with optional overwrite. |
| `/Admin/People`, row Native field policies button | `persons.read` / `persons.update` | Manage the Person's independent field state. |

UI assignment also needs `settings.read` to load source availability; target manual
protection remains available with the target permission. Policy changes apply
on the next eligible normal synchronization, with no immediate upstream fetch.
On a concurrent-change conflict, reload current policy before submitting again.
The protected API is `/api/admin/native-field-policies`: `GET sources`,
`PUT sources/{source}`, and target `GET users/{id}` / `persons/{id}` plus
`PUT users/{id}/{field}` / `persons/{id}/{field}`. Mutations require the current
revision and existing administrative authorization/cookie CSRF checks.

These policies do not control Email trust or confirmation, enrolled Email MFA
targets, recovery, IID, roles, lifecycle, credentials, usernames, IDs or bindings.
Claims Preview remains read-only. Successful normal login independently
refreshes Provider Profile snapshots. Profile uses complete source-scoped
snapshots and its existing missing/stale/failure omission rules for claims;
native missing-value preservation must not be applied to those snapshots.

Implementation: [policy options](../Core.Application/Options/NativeFieldPolicyOptions.cs),
[policy service](../Infrastructure/Services/NativeFieldPolicyService.cs) and
[admin API](../Web.IdP/Controllers/Admin/NativeFieldPoliciesController.cs).

## Identity-document identifiers (IID): implemented storage and boundaries

IID values belong to `Person`. There are no corresponding IID columns on
`ApplicationUser` in these mappings. The following are stored as hashes, not
recoverable document text:

| Document kind | Person column | Hash input |
| --- | --- | --- |
| National identity document | `NationalId` | That document's complete supplied identifier. |
| Passport | `PassportNumber` | That passport's complete supplied identifier. |
| Resident certificate | `ResidentCertificateNumber` | That certificate's complete supplied identifier. |

`PidHasher.Hash` returns null for a null/whitespace input. Otherwise it applies
`Trim()`, `ToUpperInvariant()`, UTF-8 encoding and SHA-256, then stores an
uppercase 64-character hexadecimal digest. This is deterministic, unsalted,
unkeyed hashing; it is neither reversible encryption nor a password hash/KDF.
`Verify` repeats that normalization/hash and compares the result. The same
normalized input produces the same digest; the document kind is represented
by its separate column, not by a hash domain prefix. Hash equality is data
matching evidence and does not by itself authenticate a person or establish
source assurance.

| Writer/consumer | Current handling |
| --- | --- |
| Administrative Person creation | The protected Person API accepts the document fields. `PersonService` validates new plaintext document formats, hashes them and checks uniqueness before storage. A value already shaped as 64 hex characters is retained by the existing create path. This shape test does not prove its origin. |
| Administrative Person update | Newly supplied plaintext is validated and hashed. Existing hashes are retained when no replacement plaintext is supplied; this is not an implicit document-clear operation. An actual document change resets the existing local identity-verification state. |
| General/noncanonical JIT: new account/link | Internal `ExternalAuthResult` has optional document fields. New Person documents are hashed; reuse of a Person fills only its missing document fields. This lookup can reuse a Person by a matching document hash before its optional Email matching. This is distinct from the canonical Proof path. |
| Repeat JIT on an existing exact login | The current existing-link branch can fill a missing `NationalId`; it does not refresh missing passport/resident-certificate fields. This differs from the new-account/new-link branch and must not be described as a general IID synchronizer. |
| Independent Proof 1.0 | Its allowed `AssuredProfile` contains no IID fields. The canonical Proof provisioning path does not import IID or match a Person by it. Do not add an undeclared response property and assume it will be stored. |
| Existing LDAP directory profile refresh | Its attribute list and typed `AssuredProfile` contain no IID mapping. `employeeID`, `objectGUID` and `sAMAccountName` are not IID substitutes. |
| Provider Profile 1.0 | Approved extra properties are source-scoped claims input. They are not automatically copied into the three native IID columns. A deployment must not treat an allowlisted property as permission to create or replace an IID. |
| Administrative Person response | `PersonsController` returns a masked presence indicator for each populated document field, not its plaintext or stored digest. The indicator is not replacement document input. |

`IdentityDocumentType`, `IdentityVerifiedAt` and `IdentityVerifiedBy` are separate
local verification metadata. A stored hash, confirmed Email or displayed name
does not automatically grant identity verification. Retained Legacy-specific
JIT behavior must not be assumed to authorize a new Contract/directory source.
The independently enabled recovery-proof flow has its own source evidence and
authorization rules; profile synchronization cannot bypass those gates.

Before adding an IID source, explicitly define its authority, document kind,
format/normalization, plaintext-versus-hash input contract, collision handling,
replacement/clear rules, local verification effects and permitted consumers.
Do not copy hashes between unrelated subject namespaces, put document plaintext
in ordinary logs, examples or token payloads, or use an IID as OIDC `sub`.
Stored digests also require explicit disclosure policy: the existing claim-source
allowlist intentionally permits `Person.NationalId`, so an approved claim
mapping and granted scope can disclose that stored digest. Passport and resident
certificate columns are not in that allowlist. The masked Person response is
therefore not a claim that no authorized feature can ever return a digest.
This inventory verifies application code paths; it is not a scan of
historical database contents or proof that every old row has the expected form.

Implementation: [PidHasher](../Core.Application/Utilities/PidHasher.cs),
[PersonService](../Infrastructure/Services/PersonService.cs),
[JIT](../Infrastructure/Identity/JitProvisioningService.cs) and
[Person response mapping](../Web.IdP/Controllers/Admin/PersonsController.cs).

## Boundaries for future mappings

Broader source capabilities, source clear intent, automatic fallback and IID
synchronization remain separate decisions. A future mapping must define source,
target, assurance, ownership, missing/clear semantics, timing and compatibility
tests. Historical account cleanup remains an independently scoped operation.
