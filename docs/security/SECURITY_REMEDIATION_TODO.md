# Security Remediation Backlog

This backlog tracks the reportable findings from the repository-wide static
security review of commit `e2dcf7a9538865ef7420ded42948a5afd4366ce3`
performed on 2026-07-30.

The scan reported 0 Critical, 7 High, 25 Medium, and 17 Low findings. Four
additional candidates require evidence outside the authorized static scope.
The GitHub Dependabot cross-check is also pending because authenticated
read-only access was unavailable; local NuGet and npm audits were completed.

## Delivery and Compatibility Rules

- Fix one security boundary per change set and keep unrelated refactors out.
- Preserve existing routes, request and response DTOs, successful status codes,
  audit behavior, and supported cookie and bearer authentication paths unless
  the finding requires an intentional contract change.
- Preserve legitimate administrator behavior. When a permission boundary is
  tightened, align the admin UI so it does not offer an operation that the
  current principal cannot perform.
- Add a negative regression test for the reported attack and a positive test
  for the legitimate control through the same boundary.
- Do not add migrations, deployment resets, credential rotation, or live
  environment changes unless a task explicitly requires them.
- Reassess downstream compatibility for OIDC/OAuth behavior, claims, roles,
  cookies, and deployment inputs before closing a task.

Status values: `in_progress`, `pending`, `deferred`, and `done`.

## P0 - High

| Task | Finding | Status | Acceptance criteria |
| --- | --- | --- | --- |
| H1 Role-assignment authorization boundary | `csf_22f90b83d6be6ca8c98d2e42` | done | Any IdP global-role set change requires `roles.update`; `users.update` alone cannot add or remove roles. Metadata-only user updates with an unchanged role set remain compatible. Administrators and authorized role managers retain the existing routes, DTOs, and successful responses. |
| H2 TOTP enrollment authorization | `csf_5446839cb1ab3ec3b103069a` | done | Retrieving or enabling a TOTP secret requires the intended account-management authorization and fresh authentication. The normal interactive enrollment flow remains functional. |
| H3 Recovery-code regeneration authorization | `csf_d648cf493455299a40c96ecd` | done | Replacing or retrieving recovery codes requires the intended account-management authorization and reauthentication. Existing recovery-code consumption remains compatible. |
| H4 Privileged-operation session assurance | `csf_edc0a91a588b8b31027d34dd` | done | Privileged role operations verify that the current session completed MFA rather than only checking factor enrollment. Existing properly authenticated administrator sessions continue to work. |
| H5 Email MFA possession proof | `csf_dd9a1204a8e34c5525c609a2` | done | Email MFA is not enabled and no MFA-labelled application session is issued until the pending OTP is successfully verified. |
| H6 Linked-account switching eligibility | `csf_8858d11ce2afd4b3037b3081` | done | Switching accounts applies the same user, Person, lockout, lifecycle, and MFA eligibility checks as a normal sign-in. |
| H7 Passkey sign-in eligibility | `csf_5f21ecdf3e28328eb46c9a39` | done | Passkey sign-in checks deleted and inactive state, lockout, `CanSignIn`, and Person eligibility before issuing an application cookie. |

### H1 Verification Evidence

- The pre-fix regression returned HTTP-action `Ok` for a caller with
  `users.update` but without `roles.update`; the patched path returns `Forbid`
  before the mutation service is called.
- Role additions, complete role removal, create-with-roles, and role-ID
  replacement are covered as denied cases without `roles.update`.
- Metadata-only updates with the existing role set and authorized role changes
  retain their previous successful response shapes.
- The admin role-management action now requires `users.update`, `roles.read`,
  and `roles.update`; other user-management actions keep their existing
  permission requirements.
- Focused controller tests, the full Application unit-test project, the full
  Vue test suite, a temporary-output production frontend build, the solution
  build, permission-handler tests, and the existing User CRUD system tests
  passed.

### H2 Verification Evidence

- Before the fix, a password-grant bearer token with only the existing
  `openid profile roles` scopes received HTTP 200 from the TOTP setup endpoint;
  the regression expected HTTP 403 and failed.
- Generic bearer tokens, stale application cookies, and impersonation cookies
  can no longer retrieve a TOTP secret or enable the factor.
- Self-service enrollment now starts with a CSRF-protected application-cookie
  request, signs out the current application cookie, and requires a real
  interactive sign-in. The resulting proof is user-bound, expires after five
  minutes, and is consumed after successful TOTP enrollment.
- The existing routes and successful response DTOs remain unchanged. Bearer
  enrollment is intentionally no longer supported; the repository's
  production caller used cookie authentication and now continues through the
  existing `/Account/MfaSetup` flow.
- Mandatory enrollment through the fresh `TwoFactorUserId` partial cookie
  remains functional, including setup-key retrieval.
- Focused proof-helper tests, the full Web IdP unit-test project, MFA API and
  full-flow system tests, the complete backend test suite, the full Vue test
  suite, a temporary-output production frontend build, and the solution build
  passed.

### H3 Verification Evidence

- Recovery-code replacement now explicitly uses the `Identity.Application`
  cookie context. Password accounts require the current password; passwordless
  and SSO accounts require a current TOTP proof accepted by the existing
  policy.
- A generic bearer-only request is denied before recovery-code mutation,
  disclosure, or success-audit publication. Rejected proof attempts preserve
  the existing codes and do not publish that audit event.
- A recovery-code persistence failure returns no codes and publishes no
  success audit. Existing enrollment-time code generation and recovery-code
  login and one-time consumption remain functional.
- The existing regeneration modal now uses account-appropriate proof labels,
  safe loading and error states, and matching en-US and zh-TW localization.
- Focused controller tests passed 8/8, focused service tests passed 23/23,
  and focused modal tests passed 15/15. Focused bearer-denial, regeneration,
  and disabled-MFA system flows each passed 1/1.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,328 tests with 0 failed and 1 skipped; the frontend suite
  passed 93/93. Diff validation, generated-output checks, and sensitive-output
  checks passed.

### H4 Verification Evidence

- Before the fix, password-only cookie and bearer principals belonging to an
  MFA-enrolled operator both reached privileged role persistence. The
  regression failed 2/2 with `Ok` instead of the required rejection.
- The privileged-role creation and assignment boundaries now require an MFA
  authentication-method claim from the current principal. Factor-enrollment
  flags remain limited to the separate target-enrollment policy.
- Focused tests cover cookie and bearer principals, both supported
  authentication-method claim types, TOTP and passkey sessions, the existing
  passkey policy switch, target enrollment, unprotected roles, and the
  existing disabled-policy compatibility behavior.
- Focused role and authorization tests passed 29/29; the full Application
  unit-test project passed 551/551; existing user CRUD, role assignment, and
  MFA-enabled password-grant system regressions passed 10/10.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,342 tests with 0 failed and 1 skipped. Diff validation found
  no whitespace errors, generated output, or live credential material.

### H5 Verification Evidence

- Before the fix, the partial-authentication setup endpoint accepted a direct
  enable request, persisted Email MFA without a possession proof, and promoted
  the partial principal to an `Identity.Application` session carrying
  `amr=mfa` and `amr=otp`. The negative regression failed with `Ok` instead of
  `BadRequest`.
- Both account settings and mandatory setup now send a pending code and call an
  atomic verify-and-enable service operation. Invalid, expired, missing-expiry,
  or persistence-failed proofs do not enable the factor, issue a full
  application cookie, or publish the success audit.
- The legacy direct-enable routes remain present for compatibility discovery
  but return `verificationRequired`; the repository's Vue callers now use the
  send/verify sequence with loading, resend, invalid-code, disabled, focus, and
  en-US/zh-TW states.
- The real partial-authentication HTTP flow confirms that an unverified direct
  enable receives HTTP 400, emits no `Identity.Application` cookie, and leaves
  Email MFA disabled. Existing code-consumption and replay protections remain
  functional after the enrollment contract change.
- Focused service tests passed 8/8, focused setup-controller tests passed 3/3,
  focused frontend tests passed 20/20, and the affected Email MFA system flows
  passed 23/23. The frontend suite passed 98/98.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,348 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. Diff validation found no whitespace errors, generated output, or
  live credential material.

### H6 Verification Evidence

- Before the fix, a same-Person target with an inactive user record was signed
  in successfully. The negative regression failed because the switch returned
  success and issued the target cookie instead of rejecting the request.
- Both the current and target accounts now pass the shared user, Person,
  lifecycle, lockout, and Identity `CanSignIn` checks before any sign-out,
  target sign-in, or success audit occurs.
- A switch involving an MFA-enabled account requires an MFA-authenticated
  current session. The target account also follows the existing mandatory
  enrollment policy, including TOTP, Email MFA, passkeys, notification
  persistence, and the configured grace period.
- The real cookie-and-CSRF HTTP regression creates disposable linked accounts,
  deactivates the target, verifies HTTP 403 from
  `/api/my/switch-account`, and confirms the original account remains the
  current session. Test account links are restored before cleanup.
- Focused account-management and shared login-policy tests passed 22/22;
  linked-account, AMR, and API system regressions passed 16/16.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,358 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. Diff validation found no whitespace errors, generated output, or
  live credential material.

### H7 Verification Evidence

- Before the fix, a successfully verified passkey belonging to a soft-deleted
  but still active user reached `SignInWithClaimsAsync`; the negative
  regression failed with `OkObjectResult` instead of `BadRequestObjectResult`.
- The verified-assertion path now rejects inactive or deleted users, missing
  Person links or records, Person status/date/deletion ineligibility, lockout,
  and Identity `CanSignIn` rejection before AMR session mutation, application
  cookie issuance, or last-login update.
- Existing deactivated-user and suspended-Person error behavior remains
  unchanged. An eligible active user still receives the existing nonpersistent
  cookie with the current `hwk`, `user`, and `mfa` AMR values.
- Focused passkey-controller tests passed 9/9; the full Web IdP unit-test
  project passed 173/173. Related passkey/login service tests passed 13/13,
  and existing passkey/AMR system regressions passed 9/9.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,364 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. Diff validation found no whitespace errors, generated output, or
  live credential material.

## P1 - Medium

### OAuth, OIDC, and Browser Intent

| Finding | Status | Summary |
| --- | --- | --- |
| `csf_f27142404da832c902ea5492` | fixed | Apply the current-account lifecycle predicate during authorization-code exchange. |
| `csf_14ab71c83a4dbbec064ce0c5` | fixed | Apply the current-account lifecycle predicate during device-code exchange. |
| `csf_4f7926baaf87d0ebe0ea1861` | fixed | Enforce antiforgery validation on authorization consent POST. |
| `csf_ab24e2d45bb89d24d8b45833` | fixed | Enforce antiforgery validation and one-time browser intent binding on device-verification POST. |
| `csf_2115040b736c248be7b31b82` | fixed | Protect the interactive authorization page from framing without breaking machine-readable `/connect` responses. |
| `csf_d565c43230e9a213dbf13391` | fixed | Bound authorization rate-limit partitions derived from untrusted `client_id`. |
| `csf_8ff43c021c099308b250550d` | fixed | Bound token rate-limit partitions derived from unauthenticated `client_id`. |
| `csf_c6e8b1456a3c11f02b825e11` | fixed | Pin the production public origin and reject arbitrary forwarded Host values. |

### Ownership and Administrative Data

| Finding | Status | Summary |
| --- | --- | --- |
| `csf_4bb9d23e632799503c0e50e9` | fixed | Enforce client ownership on object-specific read routes. |
| `csf_e546334e3f5b0f012de26f25` | fixed | Enforce scope ownership on update, delete, and claim-mapping routes. |
| `csf_eaec1c785f5afd7510f2a12f` | fixed | Prevent broad `settings.read` from returning decrypted sensitive settings. |
| `csf_fd26a429e678ad053edf97c1` | fixed | Never return the raw configuration-backed SMTP password as a default. |
| `csf_8e4d91cc2977abfcd9c3d276` | fixed | Allowlist custom-claim source properties so secret-bearing identity fields cannot enter tokens. |

### External Identity and Lifecycle

| Finding | Status | Summary |
| --- | --- | --- |
| `csf_46561f9a0937502b347ee99a` | fixed | Require trusted upstream email-verification evidence before JIT email binding. |
| `csf_e91772f4aee2b360fc1d4610` | fixed | Require trusted upstream email-verification evidence before external auto-linking. |
| `csf_1a0a04d1a20e4682fb094540` | fixed | Validate the expected local-user XSRF binding in the external-link callback. |
| `csf_c6b4a71a14cc93e15be81b2c` | fixed | Physically remove the Person while retaining linked ApplicationUsers as terminal denial records, revoking active local sessions atomically and blocking claims/JIT re-provisioning. |
| `csf_31193ff88cb59c04e6ff7815` | fixed | Current user/Person eligibility is checked on every Identity cookie validation and lifecycle mutations rotate linked security stamps. Post-repair independent review passed with no issues or required followups. |

### MFA, Passkeys, UI, Monitoring, and Deployment

| Finding | Status | Summary |
| --- | --- | --- |
| `csf_d27a8baae7afb246883faaee` | fixed | Generate email OTPs cryptographically and enforce a pending-code attempt limit. |
| `csf_e63c44630467bda5532dfbb8` | pending | Reject passkey login when the security policy disables passkeys. |
| `csf_65f2561e219c2e3061ca2ec9` | pending | Do not label user-verification-preferred assertions as MFA without verified UV. |
| `csf_6979042d4ed939d5baaf58aa` | pending | Prevent configured localization content from becoming anonymous stored XSS. |
| `csf_46710f5179fa498ef6327608` | fixed | Require `monitoring.read` for hub subscriptions and remove caller-invokable broadcast methods. |
| `csf_b3ba101b8e2c1014cda67044` | pending | Verify external database TLS peer identity in production setup guidance and generated configuration. |
| `csf_5f4b4b4b513c35cbf65f0b09` | pending | Remove public monitoring-port defaults and repository-known Grafana fallback credentials from operational guidance. |

### M1 Verification Evidence

- Before the fix, five invalid Email OTP submissions did not consume or
  invalidate the pending proof; the regression then accepted the original
  correct code. Code generation also used a newly constructed
  non-cryptographic `Random` instance.
- Email OTPs now use `RandomNumberGenerator.GetInt32` and invariant six-digit
  formatting. Pending codes remain password-hashed and retain the existing
  10-minute expiry, send cooldown, API response, and frontend contract.
- Every pending code has a five-attempt verification budget. A conditional
  database update reserves an attempt before password-hash verification, and
  the fifth failed attempt invalidates the matching code without revealing
  the remaining budget to the caller. Resending, successful consumption,
  expiry, user disablement, and administrator reset clear the counter.
- Additive SQL Server and PostgreSQL migrations add the non-null counter with
  a default of `0`; existing users and credentials are preserved and no
  database reset is required.
- Focused `MfaService` tests passed 27/27. Real-provider concurrency tests
  passed 2/2 and proved that 20 parallel requests against both SQL Server and
  PostgreSQL admit exactly five verifier attempts. Affected Email MFA HTTP
  regressions passed 23/23.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,367 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. Diff validation found no whitespace errors, generated application
  output, or live credential material.

### M2 Verification Evidence

- Before the fix, an authorization code issued to an active user could still
  be exchanged after that user was deactivated. The negative regression
  failed with `SignInResult` instead of OAuth `invalid_grant`.
- Authorization-code exchange now reuses the current-account lifecycle
  predicate already applied to password and refresh-token grants. It rejects
  inactive, deleted, or locked users and missing or ineligible linked Person
  records immediately before token issuance.
- Eligible users retain the existing authorization-code principal, claims,
  destinations, PKCE behavior, and token response. Legacy users without a
  Person link remain supported by the existing predicate.
- The real HTTP regression obtains a code while the seeded PKCE user is
  eligible, deactivates that account, and verifies that redemption with the
  correct verifier returns HTTP 400 JSON `invalid_grant` without access, ID,
  or refresh tokens. The account is reactivated in test cleanup.
- Focused TokenService tests passed 40/40. PKCE, authorization-code, and
  confidential-client secret-rotation system regressions passed 8/8.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,377 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M3 Verification Evidence

- Before the fix, an approved device code belonging to an inactive user still
  produced a token-issuing `SignInResult`. The negative regression failed
  instead of returning OAuth `invalid_grant`.
- Device-code polling now reuses the current-account lifecycle predicate used
  by password, refresh-token, and authorization-code grants. It rejects
  inactive, deleted, or locked users and missing or ineligible linked Person
  records immediately before token issuance.
- Eligible linked users retain the approved scopes and existing claim
  enrichment behavior. Eligible legacy users without a Person link remain
  supported.
- The real HTTP regression obtains and approves a device code, deactivates the
  user before polling `/connect/token`, and verifies HTTP 400 JSON
  `invalid_grant` without access, ID, or refresh tokens. The account is
  reactivated in test cleanup.
- Focused TokenService tests passed 50/50. The new lifecycle regression and
  existing end-to-end device client flow passed 2/2.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,388 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M4 Verification Evidence

- Before the fix, a cookie-authenticated consent submission without an
  antiforgery token returned HTTP 302 and redirected with an authorization
  code. The new real-host regression failed on that response before the
  production path was changed.
- Browser consent POSTs now require both the repository's cookie-aware
  antiforgery validation and a cryptographically random, one-time session
  intent bound to the current user and exact OpenID Connect authorization
  request.
- Missing or invalid antiforgery proofs and missing, unknown, expired,
  altered-request, wrong-user, or replayed consent intents return HTTP 400
  without redirecting to the client or issuing an authorization code.
- The intent store supports up to eight concurrent consent tabs and expires
  entries after five minutes. Rejected matching intents are consumed so they
  cannot later be reused.
- A valid allow submission retains the existing authorization-code redirect.
  Existing deny, PKCE, device-flow, AMR, and confidential-client secret
  rotation regressions also remain functional.
- Focused intent-helper tests passed 5/5, authorization owner tests passed
  8/8, the Web IdP unit-test project passed 178/178, and affected real-host
  system regressions passed 17/17.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,394 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M5 Verification Evidence

- Before the fix, a cookie-authenticated device-verification submission
  without an antiforgery token returned HTTP 200 and approved the submitted
  user code. The real-host regression failed on that response before the
  production path was changed.
- Browser device-verification POSTs now require both the repository's
  cookie-aware antiforgery validation and a cryptographically random,
  one-time session intent. Intents are bound to the current user and, when a
  valid user code is resolved while rendering the page, that exact device
  interaction.
- Manual user-code entry remains supported. Invalid user codes redisplay the
  form with a fresh intent. Missing or invalid antiforgery proofs and missing,
  unknown, expired, changed-interaction, wrong-user, or replayed intents return
  HTTP 400 without approving the interaction.
- The intent store supports up to eight concurrent verification tabs and
  expires entries after five minutes. A matching intent is consumed before
  the approval service runs.
- The real HTTP regression verifies the negative cases, a valid approval and
  success redirect, successful device-code redemption, and replay rejection
  without logging user codes, device codes, or tokens. The existing external
  device console flow and device-code lifecycle regression remain functional.
- Focused intent-helper tests passed 6/6, DeviceFlowService tests passed 3/3,
  and device-flow system tests passed 3/3.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,401 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M6 Verification Evidence

- Before the fix, the real interactive authorization consent response was
  HTML without a CSP `frame-ancestors` directive or `X-Frame-Options`. The
  real-host regression failed on the missing policy before the production
  middleware was changed.
- The security-header middleware now applies the narrow
  `frame-ancestors 'none'` CSP and `X-Frame-Options: DENY` only when the final
  `/connect/authorize` response is HTML. The path comparison is
  case-insensitive and the final response content type, rather than an
  attacker-controlled query parameter, selects the policy.
- Authorization redirects, including non-interactive responses, remain free
  of the application CSP. Machine-readable `/connect/token` errors retain
  their JSON content type and do not receive the browser-only headers.
- The real-host regression covers a mixed-case authorization path, protected
  consent HTML, the consent-denial redirect, and the token endpoint JSON
  error. Affected consent, PKCE, authorization-code, AMR, device-flow, and
  confidential-client secret-rotation system regressions passed 19/19.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,402 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M7 Verification Evidence

- Before the fix, a real ASP.NET rate-limiter middleware regression configured
  one authorization request per minute and sent two requests from the same
  source with different query `client_id` values. Both returned HTTP 200; the
  second request bypassed the expected HTTP 429 by creating a new partition.
- The authorization policy now partitions only by the connection's source IP
  at this pre-validation boundary. Raw query and form `client_id` values are
  no longer read by the partition selector, and a missing remote address
  collapses into the single bounded `ip:unknown` partition.
- Existing forwarded-header processing still runs before rate limiting when
  proxy support is enabled. Permit limits, windows, queue behavior, endpoint
  metadata, rejection status, and OpenIddict request handling are unchanged.
- The regression covers query, form, and missing `client_id` inputs: the first
  two same-source requests are allowed under a two-request limit and the third
  returns HTTP 429. The rate-limit endpoint metadata test passed 1/1, options
  binding tests passed 3/3, and affected authorization, consent, PKCE, and AMR
  system regressions passed 16/16.
- The separate unauthenticated token-endpoint partition finding remains
  pending and was intentionally not mixed into this patch.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,403 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M8 Verification Evidence

- Before the fix, a real ASP.NET rate-limiter middleware regression sent
  same-source token requests with different form `client_id` values. Changing
  the unauthenticated value created fresh partitions, so a request beyond the
  configured source budget returned HTTP 200 instead of HTTP 429.
- The token policy now partitions only by the connection's source IP at this
  pre-authentication boundary. Raw form client IDs and credential transport
  no longer select limiter state, and a missing remote address collapses into
  the single bounded `ip:unknown` partition.
- The final regression covers two distinct `client_secret_post`-style form
  client IDs, a `client_secret_basic`-style request, and a request without a
  client ID. The first three same-source requests are admitted under a
  three-request limit and the fourth returns HTTP 429. Both authorization and
  token policy regressions passed 2/2.
- Existing forwarded-header processing still runs before rate limiting when
  proxy support is enabled. Permit limits, windows, queue behavior, endpoint
  metadata, OAuth JSON errors, and OpenIddict client authentication are
  unchanged.
- Rate-limit options tests passed 3/3. Affected password, authorization-code
  and PKCE, device-code, client-credentials, secret-rotation, introspection,
  and revocation system regressions passed 27/27.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,404 tests with 0 failed and 1 existing aggressive ZAP test
  skipped.

### M9 Verification Evidence

- Before the fix, a real HTTPS request with `Host: attacker.invalid` received
  HTTP 200 from discovery, allowing OpenIddict's request-derived issuer and
  endpoint metadata to follow an untrusted Host. The regression failed because
  the expected HTTP 400 was not returned.
- Production now requires a fixed, root-level HTTPS `OpenIddict:Issuer`,
  derives the ASP.NET Core Host allowlist from that issuer, and rejects a
  supplied proxy authority that does not exactly match it. Development and
  test retain request-derived issuer support for the trusted localhost host.
- All five production Compose modes require the fixed issuer and matching
  `PUBLIC_AUTHORITY` during configuration preflight. The repository Nginx
  gateways render that authority into the redirect and upstream Host instead
  of forwarding request `Host` or `X-Forwarded-Host` values.
- The real-host regression confirms hostile Host rejection on discovery and
  authorization without a redirect, while trusted discovery retains the
  expected issuer. Existing authorization-code, PKCE, and logout regressions
  passed 12/12.
- Public-origin configuration tests passed 12/12. Bash and PowerShell setup
  syntax checks passed, and the deployment hardening harness passed all five
  production modes, including absent and empty input checks and Nginx template
  contracts.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,419 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. No database migration, reset, credential change, or live deployment
  was performed.

### M10 Verification Evidence

- Before the fix, a real authenticated ApplicationManager request could read
  another owner's client detail and received HTTP 200 instead of HTTP 403.
  The regression exercised the deployed controller and authorization stack.
- The detail, allowed-scope, required-scope, and scope-validation routes now
  apply one shared object-access policy before returning client metadata.
  IdP Administrators, explicitly enabled Development/Test trusted automation,
  and the exact Person owner retain access; other callers receive HTTP 403.
  A genuinely missing target continues to return HTTP 404.
- The focused unit authorization matrix passed 36/36 new cases across all four
  routes, including owner, administrator, trusted-automation, cross-owner,
  unowned, missing-Person, app-role, and missing-target cases. Real HTTP
  negative and same-owner positive regressions passed 2/2 without exposing
  restricted object metadata.
- Affected ownership, client CRUD, confidential-client secret rotation, PKCE,
  and logout system regressions passed 32/32. The Web IdP unit-test project
  passed 232/232.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,457 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. No database migration, DTO, frontend, OAuth/OIDC protocol, or live
  deployment change was made.

### M11 Verification Evidence

- Before the fix, real authenticated ApplicationManager requests to update
  another owner's custom scope, delete it, and replace its claim mappings all
  returned HTTP 200 instead of HTTP 403. The three failing regressions
  exercised the deployed controller and persistence services.
- Scope update, name-based delete, and claim-mapping replacement now apply one
  shared mutation policy. The exact Person owner and IdP Administrators retain
  access; other callers receive HTTP 403. Standard OIDC scopes remain writable
  only by IdP Administrators. The explicitly enabled Development/Test
  automation fixture retains access to custom scopes and has no Production
  effect.
- Existing route contracts remain intact: update and claim mapping continue to
  identify scopes by persistent ID, delete continues to accept the scope name,
  and missing-target responses preserve their previous HTTP 404 or HTTP 400
  semantics.
- The focused controller authorization matrix passed 39/39, ScopeService tests
  passed 37/37, scope authorization integration tests passed 14/14, and real
  HTTP ownership regressions passed 9/9. Existing Scope CRUD compatibility
  plus the ownership regressions passed 18/18.
- Affected scope ownership, Scope CRUD, userinfo, PKCE, and token-operation
  system regressions passed 41/41. The Web IdP unit-test project passed
  270/270.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,506 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. No database migration, DTO, frontend, OAuth/OIDC protocol, or live
  deployment change was made.

### M12 Verification Evidence

- Before the fix, a real authenticated test wrote a runtime-only protected
  secret through the settings update API and then read it through the exact-key
  endpoint. The response was not the expected `(set)` marker, proving that the
  endpoint reflected the value returned by the decrypting settings service.
  The failing assertion and test output did not include the secret value.
- Exact-key reads now replace every non-empty password- or secret-classified
  value with the existing `(set)` presence marker. Empty sensitive settings
  remain empty, and non-sensitive exact-key reads retain their original value
  and response shape.
- Internal settings encryption, decryption, caching, configuration resolution,
  update authorization, and the `(set)` preservation behavior are unchanged;
  only the administrative read response is shaped at the controller boundary.
- Focused SettingsController tests passed 12/12, SettingsService tests passed
  16/16, and the real exploit regression passed 1/1. Settings CRUD, mail, and
  related administrative system regressions passed 18/18; the Web IdP unit
  project passed 275/275.
- The separate configuration-backed SMTP default reflection finding remains
  pending and was intentionally not mixed into this database-backed exact-key
  response patch.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,512 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. No database migration, DTO, frontend, OAuth/OIDC protocol, or live
  deployment change was made.

### M13 Verification Evidence

- Before the fix, a focused controller regression supplied a non-empty SMTP
  password through an in-memory configuration source. The effective `value`
  was masked, but `defaultValue` was not the expected `(set)` marker, proving
  that the Mail prefix projection exposed the configuration-backed value. The
  assertion and test output did not include that value.
- Mail prefix responses now apply the existing sensitive-key classification to
  both the effective value and configuration-backed default. A configured SMTP
  password is represented only as `(set)`; an unset password remains empty.
- Non-sensitive defaults such as SMTP host remain unchanged. The existing
  Email UI continues to consume the effective `value`, recognize `(set)`, and
  preserve the current password when that marker is submitted. Settings
  resolution, storage, encryption, mail dispatch, DTOs, and authorization are
  unchanged.
- Focused SettingsController tests passed 13/13, and the real authenticated
  Mail prefix HTTP regression passed 1/1. Settings CRUD, mail, and related
  administrative system regressions passed 19/19; the Web IdP unit project
  passed 276/276.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,514 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. No database migration, frontend, OAuth/OIDC protocol, deployment
  configuration, or live credential change was made.

### M14 Verification Evidence

- Before the fix, a token-service integration test persisted a custom claim
  mapping whose source was a runtime-only Email MFA value. The unrestricted
  reflection path added that value under an attacker-selected claim type to
  the token principal. The assertion and test output did not include the
  value.
- Claim source resolution now uses explicit accessors for approved
  `ApplicationUser` and `Person` profile properties. Unsupported persisted
  paths are skipped before a claim is created, and claim create/update APIs
  reject unsupported paths before persistence or partial mutation.
- Every approved path has policy-level resolution coverage and token-principal
  integration coverage, including access-token and identity-token
  destinations. The existing `Person.FirstName` mapping remains functional;
  credential, MFA, recovery, lockout, navigation, lifecycle, and audit paths
  are rejected.
- ClaimsService and source-policy tests passed 40/40, ClaimsEnrichment token
  integration tests passed 3/3, and ClaimsEnrichment unit tests passed 5/5.
  Claims/Scope/Userinfo/Token system regressions passed 38/38, the full
  frontend suite passed 98/98, and a production Vite build to an isolated
  system-temp directory succeeded with only existing warnings.
- The solution build passed with 0 warnings and 0 errors; the full backend
  suite passed 1,535 tests with 0 failed and 1 existing aggressive ZAP test
  skipped. No database migration, deployed configuration, live credential, or
  live VM change was made.

### M15 Verification Evidence

- Before the fix, four untrusted external-email variants reached the
  existing-account email lookup in `ExternalLoginCallback`.
- Automatic matching-email account selection or linking now requires the
  existing provider-specific trusted email assurance policy before that lookup.
  Durable provider-key sign-in remains earlier and independent of current email
  assurance; explicit local-credential-protected linking remains independent.
- Focused callback and explicit-link security tests passed 9/9, and the Web
  IdP unit-test project passed 289/289. `git diff --check` passed for the owned
  implementation and test files.

### M16 Verification Evidence

- `Web.IdP/Controllers/Account/LinkExternalLoginController.cs` resolves the
  current local user and supplies that identifier to the framework-supported
  `GetExternalLoginInfoAsync(expectedXsrf)` validation before provider policy
  evaluation or `AddLoginAsync`.
- Missing or mismatched callback context is rejected without provider-policy or
  linking side effects; a matching current-user context continues to link.
- Focused `LinkExternalLoginControllerSecurityTests` passed 3/3, and the
  solution build passed with 0 warnings and 0 errors.

### M17 Verification Evidence

- Person hard delete physically removes the Person while retaining each linked
  ApplicationUser as an inactive/deleted terminal denial record, with a rotated
  SecurityStamp. External-login and passkey bindings are retained; no account,
  credential, or migration is removed or added.
- A relational Serializable transaction atomically terminalizes linked users,
  revokes active local UserSession records, and removes the Person. SQLite
  success and injected late-DELETE failure tests prove complete success state
  and rollback without affecting unrelated users. DELETE `204`/`404`,
  post-commit audit placement, and soft-delete/status behavior remain intact.
- Terminal users are rejected before claims/base-principal generation or orphan
  auto-heal and before JIT Person creation, mutation, or linking; active
  eligible orphan auto-heal and legitimate JIT flows remain functional.
- Focused Infrastructure and Application tests passed 52 and 18; full
  Infrastructure, Application, and Web tests passed 285, 594, and 292.
  The solution build passed with 0 warnings and 0 errors. `dotnet list package
  --vulnerable --include-transitive` found no vulnerable test packages; the
  SQLite bundle and library are 2.1.12.
- Application-cookie current-state validation now rejects lifecycle-ineligible
  sessions independently of the configured security-stamp interval. It checks
  inactive, soft-deleted, and currently locked-out users plus linked Person
  soft deletion, non-`Active` status, future start dates, and expired end
  dates. Self-contained access JWT lifetime semantics are unchanged.

### M18 Verification Evidence

- `csf_31193ff88cb59c04e6ff7815` is implemented with current-state Identity
  cookie validation and same-save-boundary linked-user security-stamp rotation
  for scoped user and Person lifecycle mutations. There is no schema
  migration, UserSession redesign, endpoint, or production certificate change.
- The system-test fixture uses an ephemeral localhost certificate only because
  a local developer certificate cannot be assumed. It does not modify the
  certificate store and cleans up its temporary material.
- Post-repair focused delta evidence passed: PersonLifecycle 7/7;
  lifecycle-cookie 3/3; login lockout 1/1; and the solution build completed
  with 0 warnings and 0 errors. Mandatory independent review passed with no
  issues or required followups. The full backend solution suite
  passed: Application 595 passed; Infrastructure Unit 293 passed; Web.IdP Unit
  301 passed; Infrastructure Integration 86 passed; System 305 passed and 1
  skipped.

### M19 Verification Evidence

- Before the fix, anonymous and authenticated-without-permission SignalR
  negotiation both returned HTTP 200. The Hub also exposed three public
  methods that rebroadcast caller-supplied monitoring DTOs.
- `/monitoringHub` now requires `monitoring.read` using the existing
  Identity-cookie and OpenIddict bearer authentication schemes. Anonymous
  negotiation returns HTTP 401 and an authenticated principal without the
  permission receives HTTP 403 without an interactive login redirect.
- The caller-invokable broadcast methods were removed. Authorized connections
  retain the existing route, `monitoring` group, and client event names;
  trusted background and monitoring services continue publishing through
  `IHubContext<MonitoringHub>`.
- Focused Hub security tests passed 1/1, MonitoringService tests passed 8/8,
  and monitoring/admin system regressions passed 22/22. Both authorized
  bearer and Identity-cookie negotiation controls return HTTP 200.
- The solution build passed with 0 warnings and 0 errors. The full backend
  suites passed: Application 595, Infrastructure Unit 295, Web.IdP Unit 301,
  Infrastructure Integration 86, and System 310; one aggressive ZAP System
  test remained skipped. No migration, frontend contract, live deployment,
  database, credential, or ignored deployment override was changed.

## P2 - Low Hardening

| Finding | Status | Summary |
| --- | --- | --- |
| `csf_b052652dfb855342d00c7616` | pending | Neutralize spreadsheet formulas and correctly quote audit CSV fields. |
| `csf_e90bcd3f0350f0d6e81b5ce9` | pending | Avoid all-interface split-host gateway defaults. |
| `csf_654c771187626cf6f26c8f37` | pending | Protect seeded SMTP credentials with the sensitive-setting protector. |
| `csf_e3c4898e191a7db50e953da6` | pending | Narrow application binding and forwarded-header trust defaults. |
| `csf_e9b4261aa3d035db5a33f996` | pending | Validate the MFA enrollment completion return URL. |
| `csf_f78a425cb2a2c60f43f40c14` | pending | Reject slash-backslash network-path login redirects. |
| `csf_5ae48e412b4ed7cf43feb514` | pending | Keep live database passwords out of command-line arguments in documentation. |
| `csf_85f765104c94681892ff0387` | pending | Make single-use MFA credential consumption atomic and check persistence results. |
| `csf_5afcc4091262878d6ad864e2` | pending | Pin third-party scripts with integrity metadata or self-host them. |
| `csf_d68de0f409f12da4a08f2653` | pending | Create backup archives with restrictive permissions. |
| `csf_7f0ae77c761a012380471239` | pending | Stop printing caller-supplied database connection strings. |
| `csf_36cc6040dad01c9d4a7e3391` | pending | Pin write-capable GitHub Actions to immutable revisions. |
| `csf_b9261fe49ea7427579600b35` | pending | Create generated deployment environment files with restrictive permissions. |
| `csf_ce51dfebb42fa33d6977ad2c` | pending | Encrypt or strictly protect maintenance archives containing operational secrets. |
| `csf_345d837ec3ca1e07626e0652` | pending | Remove copyable external-login examples that bypass two-factor checks. |
| `csf_e72deac61f2553025c97014e` | pending | Support immutable image digests or verified signatures for remote deployment. |
| `csf_e90dde79cc231e86efafaaa7` | pending | Require verified TLS in remote database migration examples. |

## Deferred Evidence

| Candidate | Status | Required evidence |
| --- | --- | --- |
| `candidate-6f0216b32ee92fd6` | deferred | Current confidential-client persistence and revocation evidence, without retrieving or reproducing credential values. |
| `candidate-2f3b58afd5c9367a` | deferred | Registration confirmation policy and pre-confirmation session capabilities. |
| `candidate-4ed3635eb0b4fe3a` | deferred | A shipped response-controlled dangerous URI and platform-dispatch path. |
| `candidate-923760322237131f` | deferred | Database-reader reachability and a practical identifier corpus. |
| Dependabot cross-check | deferred | Authenticated read-only GitHub access to enumerate currently open alerts and reconcile them with local package-manager audits. |
