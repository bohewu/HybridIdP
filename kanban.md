# HybridAuth IdP Kanban

_Derived from `todo-ledger.json`; updated 2026-08-06T00:49:42Z._

## Done

### SEC-20260728-admin-bootstrap — Fail-closed privileged test-admin bootstrap and operational guidance

- Completed: 2026-07-29T04:06:19Z
- Run: `pipeline-20260729T024815Z-bootstrap-critical`
- Notes: Title: Fail-closed privileged test-admin bootstrap and operational guidance Summary: Provider-correct reserved bootstrap-marker mutation guard, controlled admin API rejection, binding-safe disabled environment example, and focused bootstrap/default-admin regressions completed. Evidence: Focused settings-controller regressions 7/7 passed; Provider equality and persistence regressions 16/16 passed, including SQL Server case-equivalent rejection/no mutation and PostgreSQL casing-only allowance; Bootstrap endpoint/login regressions 16/16 passed, including disabled environment-example options binding and default-closed endpoint; Bootstrap/password/privileged compatibility regressions 74/74 passed; Solution build passed with 0 errors; Final review passed with required_followups empty; Full solution test had 2 unrelated local SQL Server TLS fixture failures; focused remediation coverage passed Artifacts: .pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/review-report.json, .pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/test-report.json, .pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/status/tasks/T1.json, .pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/task-list.json, .pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/delta-task-list-round1.json Note: Completed Critical OSS bootstrap/default-admin remediation synced without importing the optional TLS environment followup or unrelated findings.
- Artifacts:
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/review-report.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/status/tasks/T1.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/delta-task-list-round1.json`

### SEC-20260728-deployment-hardening — Fail-closed deployment configuration and private data services

- Completed: 2026-07-28T08:21:52Z
- Run: `20260728-deployment-hardening`
- Notes: Title: Fail-closed deployment configuration and private data services Summary: Deployment hardening completed across all five production modes with explicit local operator overrides preserved. Evidence: Five production modes fail closed when required deployment configuration or database secrets are missing; Database and Redis services have no default host ports; Explicit loopback override remains available for local operator access; Deployment-hardening harness, focused tests, static checks, and final review passed with required_followups empty Artifacts: .pipeline-output/20260728-deployment-hardening/pipeline/review-report.json, .pipeline-output/20260728-deployment-hardening/pipeline/test-report.json, .pipeline-output/20260728-deployment-hardening/status/tasks/T1.json, .pipeline-output/20260728-deployment-hardening/status/tasks/T2.json, .pipeline-output/20260728-deployment-hardening/status/tasks/T3.json Note: Local commit remains pending; unrelated scan findings and deferred work were not imported.
- Artifacts:
  - `.pipeline-output/20260728-deployment-hardening/pipeline/review-report.json`
  - `.pipeline-output/20260728-deployment-hardening/pipeline/test-report.json`
  - `.pipeline-output/20260728-deployment-hardening/status/tasks/T1.json`
  - `.pipeline-output/20260728-deployment-hardening/status/tasks/T2.json`
  - `.pipeline-output/20260728-deployment-hardening/status/tasks/T3.json`

### SEC-20260729-client-secret-rotation — Confidential-client secret rotation verification

- Completed: 2026-07-29T04:27:08Z
- Run: `pipeline-20260729T042708Z-client-secret-rotation`
- Notes: Title: Confidential-client secret rotation verification Summary: Verified confidential-client secret rotation end to end: current credentials authenticate through client_secret_post and client_secret_basic, superseded credentials are rejected immediately, regeneration and metadata/atomicity behavior hold, and the rotated credential completes the PKCE/session/logout lifecycle. Evidence: Production descriptor-overload fix was already present in commit dee395db; existing ClientService unit coverage proves descriptor selection, null-secret preservation, ordering, and failure behavior; ConfidentialClientSecretRotationSystemTests passed in isolation with successful replacement and regenerated credential probes through client_secret_post and client_secret_basic and immediate superseded-credential rejection; Invalid-update atomicity, metadata-only secret preservation, one-time regeneration output, rotated-credential Authorization Code with S256 PKCE login/code-redemption/session/logout, cleanup, and sensitive-output hygiene checks passed; Affected PKCE, logout, and client CRUD system-test filter passed: 23 tests; solution build passed with zero errors; Final review passed with required_followups empty; Full solution test was partial only because seven unrelated AdminMiscEndpointTests hit the local SQL Server TLS fixture failure; no new product todo was created Artifacts: .pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/review-report.json, .pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/test-report.json, .pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/task-list.json, .pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/repo-findings.json Note: Delivery was scoped to verification and test coverage; unrelated local SQL TLS fixture failures remain environmental evidence only.
- Artifacts:
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/review-report.json`
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/repo-findings.json`

### SEC-20260729-client-ownership-authorization — Fail-closed client ownership authorization and trusted automation

- Completed: 2026-07-29T06:09:18Z
- Run: `pipeline-20260729T060918Z-client-ownership`
- Notes: Title: Fail-closed client ownership authorization and trusted automation Summary: Closed the High client-ownership authorization gap across client update (including Permissions and scope permissions), secret regeneration or rotation, allowed-scope replacement, and required-scope replacement; trusted test automation now requires a host-controlled closed-by-default bootstrap context, while cross-owner and unrecognized service-principal callers are denied before side effects. Evidence: ClientsController focused unit coverage passed 50/50, including caller classification, all four mutation guards, 403 denial before mutation services, no-effects checks, and preserved same-owner, Admin-role, and trusted-automation success behavior; ClientOwnershipAuthorizationSystemTests passed 7/7 with realistic HTTP coverage for same-owner, cross-owner, Admin-role, recognized testclient-admin automation, and unrecognized service-principal denial; denied state and credential effects were unchanged and 200/400/403/404/423 semantics remained distinct; Full Web.IdP unit suite passed 118/118; ClientCrudTests passed 18/18; ConfidentialClientSecretRotationSystemTests passed 1/1; HybridAuthIdP.sln build passed with 0 errors (42 warnings reported); Round1 review passed with overall_status pass, required_followups empty, and scope, cleanup, and sensitive-output audits passing; Full solution test was not rerun because the previously reproduced unrelated local SQL Server TLS/certificate fixture-startup failure remains environmental; all affected focused round1 gates passed Artifacts: .pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/problem-spec.json, .pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/task-list.json, .pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/delta-task-list-round1.json, .pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/test-report-round1.json, .pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/review-report-round1.json Note: The unrelated local SQL Server TLS/certificate fixture limitation is evidence only; no remaining task or blocker was created, and unrelated findings or fixture literals were not imported as work.
- Artifacts:
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/problem-spec.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/delta-task-list-round1.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/test-report-round1.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/review-report-round1.json`

### SEC-20260730-h3-recovery-codes — H3 recovery-code regeneration reauthentication and persistence ordering

- Completed: 2026-07-30T10:43:26Z
- Run: `pipeline-20260730T085128Z-h3-recovery-codes`
- Priority: high
- Notes: Validated H3 fix: recovery-code regeneration requires an explicit Identity.Application session plus current password for password accounts or current policy-accepted TOTP for passwordless/SSO accounts; persistence succeeds before one-time disclosure and MfaRecoveryCodesRegenerated audit publication. Focused controller tests 8/8, MfaService tests 23/23, modal tests 15/15, bearer-denial/regeneration/disabled-MFA system flows 1/1 each, full frontend 93/93, full backend 1,328 passed and 1 skipped, solution build 0 warnings and 0 errors, review pass with no required followups, diff and sensitive-output checks passed. H2 TOTP enrollment remains done from commit ad6530f0; H4 and H5 remain untouched.
- Artifacts:
  - `.pipeline-output/pipeline-20260730T085128Z-h3-recovery-codes/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260730T085128Z-h3-recovery-codes/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260730T085128Z-h3-recovery-codes/pipeline/review-report.json`

### SEC-20260803-external-email-assurance — Trusted external email assurance for JIT binding and callback auto-linking

- Completed: 2026-08-03T02:20:45Z
- Run: `pipeline-20260803T013155Z-external-autolink`
- Priority: medium
- Source: `pipeline`
- Related tasks: `task-implement-trusted-email-auto-link`, `task-document-remediation-trust-boundary`
- Notes: Both scan findings csf_46561f9a0937502b347ee99a and csf_e91772f4aee2b360fc1d4610 are closed through provider-specific trusted email assurance. JIT email/Person binding and automatic callback email matching reject missing/false/unsupported assurance; durable provider-key sign-in, trusted matching, and explicit local-credential linking remain compatible. Evidence: post-review focused tests 16/16, Web unit tests 290/290, Application unit tests 591/591, solution build 0 warnings/errors, diff check pass, final review pass. System/live provider tests not run; no live VM or credentials touched.
- Artifacts:
  - `pipeline/problem-spec.json`
  - `pipeline/task-list.json`
  - `pipeline/test-report.json`
  - `pipeline/review-report.json`

### SEC-20260803-external-link-xsrf-binding — External-account linking local-user XSRF binding validation

- Completed: 2026-08-03T03:23:21Z
- Run: `pipeline-20260803T025013Z-external-link-xsrf`
- Priority: medium
- Source: `pipeline`
- Related tasks: `T1`, `T2`
- Notes: Scope/acceptance: Explicit external-account linking validates the expected local-user XSRF binding before provider-limit policy or AddLoginAsync, rejects missing/mismatched/unauthenticated contexts without linking side effects, and preserves same-user linking, status/error semantics, and external-cookie cleanup; remediation documentation and tracking were updated. Evidence: exact expectedXsrf enforcement before provider policy/AddLoginAsync; focused regression 3/3; full Web unit suite 292/292; HybridAuthIdP.sln build 0 warnings/errors; mandatory review passed with no required followups; no live VM, provider, or system test was run.
- Artifacts:
  - `.pipeline-output/pipeline-20260803T025013Z-external-link-xsrf/pipeline/problem-spec.json`
  - `.pipeline-output/pipeline-20260803T025013Z-external-link-xsrf/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260803T025013Z-external-link-xsrf/tasks/T1/result.json`
  - `.pipeline-output/pipeline-20260803T025013Z-external-link-xsrf/tasks/T2/result.json`
  - `.pipeline-output/pipeline-20260803T025013Z-external-link-xsrf/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260803T025013Z-external-link-xsrf/pipeline/review-report.json`

### SEC-20260803-person-hard-delete-terminalization — Safe terminalization of linked accounts on Person hard delete

- Completed: 2026-08-03T04:42:38Z
- Run: `pipeline-20260803T033054Z-person-hard-delete`
- Priority: medium
- Source: `pipeline`
- Related tasks: `T1`, `T2`, `T3`
- Notes: Scope/evidence: Serializable atomic terminalization, session revocation, and Person removal; injected late-DELETE failure proves rollback; claims and JIT deny terminal users before mutation or recreation. Focused tests 52 Infrastructure + 18 Application; full tests 285 Infrastructure + 594 Application + 292 Web; solution build passed with 0 warnings/errors; vulnerable-list clean with SQLite 2.1.12; mandatory review passed. No live VM/provider/database/system test was run. Residuals: cookie validation interval remains csf_31193ff88cb59c04e6ff7815, and existing access JWTs may last to expiry.
- Artifacts:
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/pipeline/problem-spec.json`
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/tasks/T1/result.json`
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/tasks/T2/result.json`
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/tasks/T3/result.json`
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260803T033054Z-person-hard-delete/pipeline/review-report.json`

### SEC-20260803-lifecycle-cookie-invalidation — Lifecycle-cookie invalidation for ineligible users and Persons

- Completed: 2026-08-03T09:07:36Z
- Run: `pipeline-20260803T065639Z-lifecycle-cookie`
- Priority: medium
- Source: `pipeline`
- Related tasks: `T1`, `T2`, `T3`
- Notes: M18 / csf_31193ff88cb59c04e6ff7815 completed in run pipeline-20260803T065639Z-lifecycle-cookie. Evidence: full backend suite passed; post-repair review passed with no required followups; repository remediation entry fixed.
- Artifacts:
  - `.pipeline-output/pipeline-20260803T065639Z-lifecycle-cookie/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260803T065639Z-lifecycle-cookie/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260803T065639Z-lifecycle-cookie/pipeline/review-report-round-1.json`

### SEC-20260804-passkey-policy-uv — Passkey policy enforcement and UV-accurate MFA classification

- Completed: 2026-08-04T03:05:51Z
- Run: `passkey-medium-20260804T014744Z-2e82e9cb`
- Priority: medium
- Source: `pipeline`
- Related tasks: `passkey-policy-and-uv-remediation`, `passkey-remediation-documentation`
- Notes: Both findings csf_e63c44630467bda5532dfbb8 and csf_65f2561e219c2e3061ca2ec9 are remediated: disabled passkey completion is rejected before assertion verification or success side effects, and MFA/AMR classification is set only from validated user-verification (UV) evidence while eligible passkey behavior remains compatible. Evidence: focused infrastructure 13/13, controller 11/11, PasskeyApi 8/8, full backend 1594 passed/1 skipped/0 failed, solution build 0 warnings/errors, review pass, exact 11-path scope, and teardown clean (0 dotnet-owned TCP listeners; no Web.IdP/TestHost process remained); git diff --check pass. Localization, deployment, the P3 documentation note, and unrelated findings remain out of scope.
- Artifacts:
  - `.pipeline-output/passkey-medium-20260804T014744Z-2e82e9cb/pipeline/task-list.json`
  - `.pipeline-output/passkey-medium-20260804T014744Z-2e82e9cb/pipeline/test-report-attempt2.json`
  - `.pipeline-output/passkey-medium-20260804T014744Z-2e82e9cb/pipeline/review-report.json`
  - `.pipeline-output/passkey-medium-20260804T014744Z-2e82e9cb/status/tasks/passkey-policy-and-uv-remediation.json`
  - `.pipeline-output/passkey-medium-20260804T014744Z-2e82e9cb/status/tasks/passkey-remediation-documentation.json`

### SEC-20260804-loginnote-localization-xss — LoginNotice localization stored-XSS remediation

- Completed: 2026-08-04T04:03:30Z
- Run: `localization-medium-20260804T031736Z-8da3ce62`
- Priority: medium
- Source: `pipeline`
- Related tasks: `remediate-loginnote-rendering-xss`, `document-loginnote-xss-remediation`
- Notes: Finding csf_6979042d4ed939d5baaf58aa is remediated by HTML-encoding untrusted LoginNotice localization at the shared Razor rendering boundary while preserving resolver, storage, and authorization behavior. Evidence: real Razor 5/5, resolver 7/7, localization services 11/11, auth system 2/2, full backend 1,599 passed/1 skipped/0 failed, solution build 0 warnings/errors, review pass, exact four-path scope, git diff --check pass, temporary environment variables restored/unset, and no task-attributed dotnet/Web.IdP processes or listeners remained. Passkey, deployment, and unrelated findings remain out of scope.
- Artifacts:
  - `.pipeline-output/localization-medium-20260804T031736Z-8da3ce62/pipeline/task-list.json`
  - `.pipeline-output/localization-medium-20260804T031736Z-8da3ce62/pipeline/test-report.json`
  - `.pipeline-output/localization-medium-20260804T031736Z-8da3ce62/pipeline/review-report.json`
  - `.pipeline-output/localization-medium-20260804T031736Z-8da3ce62/status/tasks/remediate-loginnote-rendering-xss.json`
  - `.pipeline-output/localization-medium-20260804T031736Z-8da3ce62/status/tasks/document-loginnote-xss-remediation.json`

### SEC-20260804-external-db-tls — Authenticated TLS for external database setup wizard output

- Completed: 2026-08-04T05:23:46Z
- Run: `deployment-medium-20260804T041444Z-7f3c2d19`
- Priority: medium
- Source: `pipeline`
- Related tasks: `task-external-db-tls-wizards`
- Notes: Finding csf_b3ba101b8e2c1014cda67044 is fixed in both setup wizards: new external SQL Server output uses Encrypt=True;TrustServerCertificate=False and PostgreSQL uses SSL Mode=VerifyFull with system trust or /app/certs CA material; invalid TLS inputs fail closed. An existing operator-managed .env remains byte-for-byte preserved when replacement is declined, and internal Docker connection strings/networking remain unchanged. Deterministic deployment contracts, syntax/parser, diff/scope, and cleanup checks passed; test-report and strong review passed with required_followups empty. No live VM, ignored .env, credentials, migrations/data, or push action was used.
- Artifacts:
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/problem-spec.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/task-list.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/status/tasks/task-external-db-tls-wizards.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/test-report.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/review-report.json`
  - `docs/security/SECURITY_REMEDIATION_TODO.md`

### SEC-20260804-monitoring-private-defaults — Private-by-default monitoring examples with explicit Grafana credentials

- Completed: 2026-08-04T05:23:46Z
- Run: `deployment-medium-20260804T041444Z-7f3c2d19`
- Priority: medium
- Source: `pipeline`
- Related tasks: `task-monitoring-markdown-contract`
- Notes: Finding csf_5f4b4b4b513c35cbf65f0b09 is fixed in both MAINTENANCE_GUIDE compose examples: Grafana and log-store ports are loopback-only by default, a non-empty GRAFANA_PASSWORD is required, internal Docker connectivity remains compatible, and PowerShell rendering is corrected. Deterministic monitoring static validator, syntax/parser, diff/scope, and cleanup checks passed; test-report and strong review passed with required_followups empty. No live VM, ignored .env, credentials, migrations/data, or push action was used.
- Artifacts:
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/problem-spec.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/task-list.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/status/tasks/task-monitoring-markdown-contract.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/test-report.json`
  - `.pipeline-output/deployment-medium-20260804T041444Z-7f3c2d19/pipeline/review-report.json`
  - `docs/security/SECURITY_REMEDIATION_TODO.md`

### HIDP-20260806-1 — Generic OSS upstream-authentication boundary audit/spec

- Completed: 2026-08-06T00:49:42Z
- Run: `flow-20260806T081830Z-hybrididp-ad-boundary`
- Related tasks: `HIDP-20260806-1-auth-contract`, `HIDP-20260806-1-doc-consistency`, `HIDP-20260806-1-static-doc-review`
- Notes: Docs-only audit/spec defines preferred future direct configurable AD/LDAP, an optional provider-neutral authentication/profile API only for an explicitly documented directory capability gap, explicit fail-closed provider selection, directory versus HybridIdP authority, and assured linking, claims, lifecycle, and session boundaries. Evidence: six reviewed docs; normalized forbidden-term scan zero; JSON, diff, and changed-scope checks passed; final reviewer PASS with no findings/followups; no .NET or connected AD tests run by design; delivery uses one focused local commit; no push.
- Artifacts:
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/problem-spec.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/task-list.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/synthesis.md`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/static-doc-review.md`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/review-report.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/status/tasks/HIDP-20260806-1-auth-contract.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/status/tasks/HIDP-20260806-1-doc-consistency.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/status/tasks/HIDP-20260806-1-static-doc-review.json`
  - `README.md`
  - `docs/ARCHITECTURE.md`
  - `docs/AUTHENTICATION_INTEGRATION.md`
  - `docs/SECURITY.md`
  - `docs/TODOS.md`
  - `docs/design_specs/phase-23-ad-integration-plan.md`

## Backlog

### HIDP-20260806-2 — Implement and test generic upstream-provider contract with configurable direct AD/LDAP adapter

- Status: backlog
- Run: `flow-20260806T081830Z-hybrididp-ad-boundary`
- Related tasks: `HIDP-20260806-1`
- Notes: Pending implementation and tests for a generic upstream-provider contract plus a deployment-configurable direct AD/LDAP adapter, preserving OSS and non-institutional scope. A standardized provider-neutral API adapter remains conditional on a proven directory capability gap and is not automatic scope.
- Artifacts:
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/problem-spec.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/task-list.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/synthesis.md`

### HIDP-20260806-3 — Opt-in sanitized connected non-production AD validation

- Status: backlog
- Run: `flow-20260806T081830Z-hybrididp-ad-boundary`
- Related tasks: `HIDP-20260806-2`
- Notes: Pending opt-in sanitized connected validation in a non-production directory environment, dependent on HIDP-20260806-2. Use no production credentials or data, and never perform an automatic writable retry.
- Artifacts:
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/problem-spec.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/task-list.json`
  - `.pipeline-output/flow-20260806T081830Z-hybrididp-ad-boundary/flow/synthesis.md`
