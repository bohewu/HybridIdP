# HybridAuth IdP Kanban

_Derived from `todo-ledger.json`; updated 2026-07-29T04:06:19Z._

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
