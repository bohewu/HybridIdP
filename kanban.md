# HybridAuth IdP Kanban

_Derived from `todo-ledger.json`; updated 2026-07-29T06:09:18Z._

## Done

### SEC-20260728-admin-bootstrap — Fail-closed privileged test-admin bootstrap and operational guidance

- Completed: 2026-07-29T04:06:19Z
- Run: `pipeline-20260729T024815Z-bootstrap-critical`
- Summary: Provider-correct reserved bootstrap-marker mutation guard, controlled admin API rejection, binding-safe disabled environment example, and focused bootstrap/default-admin regressions completed.
- Evidence: Focused settings-controller regressions 7/7 passed; provider equality and persistence regressions 16/16 passed, including SQL Server case-equivalent rejection/no mutation and PostgreSQL casing-only allowance; bootstrap endpoint/login regressions 16/16 passed, including disabled environment-example options binding and default-closed endpoint; bootstrap/password/privileged compatibility regressions 74/74 passed; solution build passed with 0 errors; final review passed with required_followups empty; full solution test had 2 unrelated local SQL Server TLS fixture failures; focused remediation coverage passed.
- Artifacts:
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/review-report.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/status/tasks/T1.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260729T024815Z-bootstrap-critical/pipeline/delta-task-list-round1.json`
- Note: Completed Critical OSS bootstrap/default-admin remediation synced without importing the optional TLS environment followup or unrelated findings.

### SEC-20260728-deployment-hardening — Fail-closed deployment configuration and private data services

- Completed: 2026-07-28T08:21:52Z
- Run: `20260728-deployment-hardening`
- Summary: Deployment hardening completed across all five production modes with explicit local operator overrides preserved.
- Evidence: Five production modes fail closed when required deployment configuration or database secrets are missing; database and Redis services have no default host ports; explicit loopback override remains available for local operator access; deployment-hardening harness, focused tests, static checks, and final review passed with required_followups empty.
- Artifacts:
  - `.pipeline-output/20260728-deployment-hardening/pipeline/review-report.json`
  - `.pipeline-output/20260728-deployment-hardening/pipeline/test-report.json`
  - `.pipeline-output/20260728-deployment-hardening/status/tasks/T1.json`
  - `.pipeline-output/20260728-deployment-hardening/status/tasks/T2.json`
  - `.pipeline-output/20260728-deployment-hardening/status/tasks/T3.json`
- Note: Local commit remains pending; unrelated scan findings and deferred work were not imported.

### SEC-20260729-client-secret-rotation — Confidential-client secret rotation verification

- Completed: 2026-07-29T04:27:08Z
- Run: `pipeline-20260729T042708Z-client-secret-rotation`
- Summary: Verified confidential-client secret rotation end to end: current credentials authenticate through client_secret_post and client_secret_basic, superseded credentials are rejected immediately, regeneration and metadata/atomicity behavior hold, and the rotated credential completes the PKCE/session/logout lifecycle.
- Evidence: Production descriptor-overload fix was already present in commit dee395db with existing ClientService unit coverage; isolated rotation system test passed with replacement/regenerated post/basic success and immediate superseded-credential rejection; invalid-update atomicity, metadata-only preservation, one-time regeneration output, PKCE/login/code-redemption/session/logout, cleanup, and sensitive-output hygiene passed; affected PKCE/logout/client CRUD filter passed 23 tests; solution build passed with zero errors; final review passed with required_followups empty; full solution test was partial only because seven unrelated AdminMiscEndpointTests hit the local SQL Server TLS fixture failure.
- Artifacts:
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/review-report.json`
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/test-report.json`
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260729T042708Z-client-secret-rotation/pipeline/repo-findings.json`
- Note: Delivery was scoped to verification and test coverage; unrelated local SQL TLS fixture failures remain environmental evidence only.

### SEC-20260729-client-ownership-authorization — Fail-closed client ownership authorization and trusted automation

- Completed: 2026-07-29T06:09:18Z
- Run: `pipeline-20260729T060918Z-client-ownership`
- Summary: Closed the High client-ownership authorization gap across client update (including Permissions and scope permissions), secret regeneration or rotation, allowed-scope replacement, and required-scope replacement; trusted test automation now requires a host-controlled closed-by-default bootstrap context, while cross-owner and unrecognized service-principal callers are denied before side effects.
- Evidence: ClientsController focused unit coverage passed 50/50, including caller classification, all four mutation guards, 403 denial before mutation services, no-effects checks, and preserved same-owner, Admin-role, and trusted-automation success behavior; ClientOwnershipAuthorizationSystemTests passed 7/7 with realistic HTTP coverage for same-owner, cross-owner, Admin-role, recognized testclient-admin automation, and unrecognized service-principal denial; denied state and credential effects were unchanged and 200/400/403/404/423 semantics remained distinct; full Web.IdP unit suite passed 118/118; ClientCrudTests passed 18/18; ConfidentialClientSecretRotationSystemTests passed 1/1; HybridAuthIdP.sln build passed with 0 errors (42 warnings reported); round1 review passed with overall_status pass, required_followups empty, and scope, cleanup, and sensitive-output audits passing; full solution test was not rerun because the previously reproduced unrelated local SQL Server TLS/certificate fixture-startup failure remains environmental, while all affected focused round1 gates passed.
- Artifacts:
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/problem-spec.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/task-list.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/delta-task-list-round1.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/test-report-round1.json`
  - `.pipeline-output/pipeline-20260729T060918Z-client-ownership/pipeline/review-report-round1.json`
- Note: The unrelated local SQL Server TLS/certificate fixture limitation is evidence only; no remaining task or blocker was created, and unrelated findings or fixture literals were not imported as work.
