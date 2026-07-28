# HybridAuth IdP Kanban

_Derived from `todo-ledger.json`; updated 2026-07-28T08:21:52Z._

## Done

### SEC-20260728-admin-bootstrap — Fail-closed privileged test-admin bootstrap and operational guidance

- Completed: 2026-07-28T05:35:11Z
- Run: `20260728-critical-admin-bootstrap`
- Summary: T1 implemented the fail-closed privileged test-admin bootstrap and focused regressions. T2 and T2-R1 corrected live guidance, including AuthConstants references and explicit reset-database opt-in with truthful output.
- Evidence: Focused tests 16/16; owning integration tests 49/49; fixture-backed admin login system test 1/1; Web.IdP, SystemTests, and solution builds passed; PowerShell/static environment matrix passed; credential scan 0; final reviewer pass with required_followups empty.
- Artifacts:
  - `.pipeline-output/20260728-critical-admin-bootstrap/pipeline/review-report.json`
  - `.pipeline-output/20260728-critical-admin-bootstrap/status/run-status.json`
  - `.pipeline-output/20260728-critical-admin-bootstrap/status/tasks/T1.json`
  - `.pipeline-output/20260728-critical-admin-bootstrap/status/tasks/T2.json`
- Note: Completed run synced without importing unrelated scan findings, dependency warnings, or P3 notes.

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
