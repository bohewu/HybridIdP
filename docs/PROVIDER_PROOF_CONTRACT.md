# Provider Proof Contract 1.0

Provider Proof Contract 1.0 is the credential-proof boundary used by the
optional `ProviderProof` HTTP adapter. It is independent of
[Provider Metadata Contract 1.0](PROVIDER_METADATA_CONTRACT.md) and the
[Legacy Password Sync contract](PASSWORD_SYNC_CONTRACT.md). Configuring one
boundary does not configure or authorize either of the others.

## Ownership and endpoint

The proof producer validates a submitted account name and password against one
credential authority. On success it returns an assured, provider-scoped
identity. HybridIdP owns durable binding, local account and Person eligibility,
MFA, sessions, claims, tokens and consent. Proof does not make provider metadata
authoritative, assign affiliation, group or role state, or authorize a password
write.

Configure the complete endpoint URI in `ProviderProof:Endpoint`; the existing
producer route is `/api/authenticate/login`. HybridIdP sends an authenticated
`POST` with `Content-Type: application/json` and `X-Internal-Secret`. Provision
the shared secret outside source control and never put it in the URI, body or
logs. HTTPS is required unless `AllowPrivateNetworkHttp=true` explicitly permits
HTTP on a controlled private network. That switch does not validate DNS or
network placement.

Example request:

```json
{
  "accountName": "example-account",
  "password": "<submitted-password>",
  "contractVersion": "1.0",
  "requestedEmailOtpPolicy": "Disabled"
}
```

Example authenticated response:

```json
{
  "contractVersion": "1.0",
  "outcome": "Authenticated",
  "providerNamespace": "example.provider",
  "stableSubject": "opaque-subject-7f2a",
  "canonicalAccount": "example-account",
  "assurance": {
    "stableSubjectAssured": true,
    "canonicalAccountAssured": true
  },
  "profile": null,
  "requiredActions": []
}
```

## Request and response rules

`contractVersion` is `1.0`. The current request DTO defaults an omitted version
to `1.0`; an explicit unsupported version is invalid. `accountName` must be
nonblank and `password` must be nonempty. `requestedEmailOtpPolicy` is one of
`Disabled`, `ProviderRequested` or `Required`.

`outcome` is one of `Authenticated`, `InvalidCredentials`, `NotFound`,
`Disabled`, `Locked`, `Ineligible`, `Ambiguous`, `Malformed`, `Unavailable` or
`Timeout`. A non-`Authenticated` result must not contain provider identity,
profile, assurance or required actions. An authenticated result requires
nonblank `providerNamespace`, `stableSubject` and `canonicalAccount`, plus both
assurance flags set to `true`. Namespace and subject form the durable exact
provider tuple; mutable login names and email addresses are not durable keys.

The optional profile may contain only `displayName`, `givenName`, `surname`,
`email`, `department`, `title`, `employeeId` and `assuredFields`. Every nonblank
profile value must name its corresponding field in `assuredFields`; unassured
profile values invalidate the result. The only current required action is
`EmailOtp`.

## Failure, timeout and compatibility behavior

HybridIdP accepts any HTTP `2xx` response that deserializes to a valid result.
It does not separately require a response media type. A non-success HTTP status
or transport failure becomes `Unavailable`; an empty, null or invalid result
becomes `Malformed`; the configured deadline becomes `Timeout`. Caller
cancellation propagates. There is no automatic fallback to another credential
authority and no retry or version-negotiation protocol in this adapter.

The deadline defaults to five seconds and must be greater than zero and no more
than thirty seconds. A producer should emit canonical camelCase JSON and string
enum names. Contract 1.0 has no URL-version discovery, downgrade, compatibility
alias or deprecation schedule. An incompatible change requires a separate
version decision; Metadata 1.0 and the unversioned Legacy Password Sync wire contract
do not change Proof 1.0.

## Consumer configuration and implementation checklist

```text
ProviderProof__Endpoint=https://provider.example.org/api/authenticate/login
ProviderProof__SharedSecret=<PROVISION_THROUGH_SECRET_CONFIGURATION>
ProviderProof__AllowPrivateNetworkHttp=false
ProviderProof__Timeout=00:00:05
```

A third-party producer must authenticate the caller, bind the response to the
single selected proof authority, fail closed for ambiguous identity, emit no
identity data on failure, and mark every returned profile value as assured.
Keep secrets, passwords and raw response bodies out of logs. Test explicit and
omitted version behavior, every failure outcome, malformed and non-`2xx`
responses, timeout and caller cancellation before enabling an integration.

The checked-in configuration does not supply a secret or authorize a
deployment. Repository tests use synthetic HTTP handlers; they do not establish
live producer interoperability or production readiness.
