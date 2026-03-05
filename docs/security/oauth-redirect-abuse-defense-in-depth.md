# OAuth Redirect Abuse Defense-in-Depth

## Purpose

This document defines lower-layer and operational controls that complement existing backend protections against OAuth redirect abuse.

Already in place:

- Server-side redirect URI guardrails at the authorize flow (cannot be bypassed by UI manipulation).
- Dedicated throttling and probe telemetry on the authorize endpoint.

This guidance adds edge/WAF controls, SIEM monitoring, and operational playbooks for compromised-account scenarios.

## Why UI-Only Restrictions Are Not Enough

UI controls are advisory and can be bypassed by:

- Direct HTTP requests to OAuth endpoints.
- Scripted clients that never render UI.
- Replay of captured authorize requests.
- Use of valid, compromised user sessions where attacker behavior looks superficially legitimate.

Compromised-account scenario: if an attacker controls a user session, they can drive authorize flows directly and attempt redirect abuse patterns without touching the administrative UI. Therefore controls must exist at non-UI layers (application enforcement, edge/WAF, and monitoring/response).

## Layered Control Model

| Layer | Objective | Key Controls | Owner | Success Signals |
|---|---|---|---|---|
| Application guardrails | Enforce protocol correctness and redirect safety | Strict redirect URI allowlist matching, client-bound redirect validation, authorize endpoint throttling, probe telemetry with correlation IDs | Identity backend team | Rejected invalid redirect attempts, stable authorize latency/error budget |
| Edge / WAF | Detect and limit suspicious request patterns before app saturation | URI/query anomaly rules, IP and fingerprint rate controls, geo/ASN reputation controls, managed bot checks, client-specific path/domain checks | Platform/edge security | Low false positives, blocked abuse bursts, no regression in valid OAuth success rate |
| Monitoring / SIEM | Detect campaign-level abuse and account compromise indicators | Centralized authorize logs, detection rules for redirect/probe anomalies, user/IP/client correlation, high-risk alert routing | SecOps/SOC | Timely alerts, triage SLA met, reduced mean time to detect |
| Operational governance | Keep policy current and auditable | Runbooks, rollout gates, exception process, ownership matrix, periodic rule review and tabletop exercises | Security governance + service owners | Reviewed controls each release, tracked exceptions, successful incident drills |

## Edge/WAF Strategy

### Rule design principles

- Prefer staged rollout (`observe -> alert -> soft-block -> hard-block`) for every new rule.
- Tune rules per OAuth client profile where possible (public SPA vs confidential web app).
- Use allowlists for known good client IDs, redirect URI patterns, and trusted egress ranges.
- Correlate signals (rate + query anomaly + reputation) before hard blocking.

### Practical rule ideas

1. **Suspicious authorize query patterns**
   - Flag repeated `/connect/authorize` requests with unusual parameter churn from the same source (rapid changes in `client_id`, `redirect_uri`, `scope`, `response_type`, `code_challenge`).
   - Flag high-entropy or encoded `redirect_uri` values inconsistent with registered client patterns.
   - Flag repeated missing/invalid required parameters, which commonly indicates probing.

2. **Rate controls (layered with backend throttling)**
   - Per-IP and per-fingerprint ceilings for authorize requests over short windows.
   - Per-user-session and per-client burst thresholds when identifiers are available.
   - Adaptive penalties: temporary challenge/slowdown before full deny.

3. **Domain/path safety filters**
   - Enforce that `redirect_uri` host/path conforms to registered patterns (at edge when feasible, app remains source of truth).
   - Block known malicious domains/TLD patterns and disposable redirect hosts.
   - Detect open-redirect path signatures on first-party domains (for example nested `next=`, external URL parameters, double-encoding tricks).

4. **Probe and automation controls**
   - Trigger managed bot/challenge controls for high-frequency authorize probes.
   - Raise risk score for headless/browser automation indicators when combined with query anomalies.

### Important caution: preserve legitimate `prompt=none`

Do not treat `prompt=none` as inherently malicious. It is valid for silent SSO/session checks. To avoid breaking legitimate traffic:

- Baseline normal `prompt=none` volume by client before enforcement.
- Apply stricter controls only when combined with other signals (rate spikes, parameter churn, untrusted reputation, invalid redirect attempts).
- Support client-specific exceptions with expiry dates and owner approval.
- Monitor silent auth success rate and login UX regressions during rollout.

## Rollout Checklist

### Phase 1: Observe

- [ ] Enable candidate WAF rules in log-only mode.
- [ ] Capture baseline metrics: authorize RPS, `prompt=none` ratio, invalid redirect rejects, user-agent/IP distribution.
- [ ] Define service level guardrails (max acceptable false positive rate, max added latency).
- [ ] Confirm dashboards and SIEM parsers include correlation IDs.

### Phase 2: Alert

- [ ] Turn on SIEM alerts for highest-confidence detections.
- [ ] Route alerts to SOC/on-call with severity mapping and runbook links.
- [ ] Validate alert quality for at least one full business cycle.

### Phase 3: Soft-block

- [ ] Apply temporary challenge/rate-limit actions for confirmed suspicious patterns.
- [ ] Exclude known integration test ranges and trusted service accounts where documented.
- [ ] Review failed auth and support tickets daily for unintended impact.

### Phase 4: Hard-block

- [ ] Enforce deny rules for validated high-confidence abuse signatures.
- [ ] Require two-owner approval (edge security + identity service owner) for production hard-block changes.
- [ ] Keep emergency bypass toggle documented and tested.

### Rollback plan

- [ ] Maintain one-click rollback to previous WAF policy bundle.
- [ ] If false positives exceed threshold, revert from hard-block to soft-block immediately.
- [ ] Preserve forensic logs during rollback; do not disable telemetry.
- [ ] Run post-rollback review within 24 hours and retune rules before re-rollout.

### Ownership matrix

- Identity backend team: application guardrails, authorize telemetry schema, client policy source of truth.
- Platform/edge security team: WAF rules, rate/challenge policies, emergency policy rollback.
- SOC/SecOps: alert triage, incident coordination, IOC enrichment, executive escalation.
- Product/on-call operations: customer impact assessment, communication, and exception approvals.

## Incident Response Quick Actions (Suspected Redirect Abuse)

1. **Contain**
   - Move relevant WAF controls to soft-block or hard-block based on confidence.
   - Increase authorize endpoint throttling sensitivity for targeted clients/sources.

2. **Protect accounts and clients**
   - Revoke active sessions/tokens for confirmed compromised users or affected client scopes.
   - Temporarily disable high-risk client integrations if abuse is ongoing.

3. **Investigate**
   - Correlate by `client_id`, user, IP, ASN, user-agent fingerprint, and correlation ID.
   - Identify attempted redirect targets and validate whether any code/token leakage occurred.

4. **Eradicate and recover**
   - Add/refine WAF signatures and backend validations from incident findings.
   - Rotate impacted credentials/secrets where required.
   - Re-enable normal traffic progressively while monitoring false positives and abuse recurrence.

5. **Communicate and improve**
   - Notify stakeholders using incident severity protocol.
   - Publish post-incident actions with owner and due date for each control gap.

## Operational KPIs

- Invalid redirect attempts blocked (app + WAF combined).
- Authorize abuse detection mean time to detect (MTTD).
- False positive rate for WAF rules affecting authorize traffic.
- `prompt=none` success rate and end-user login completion rate during enforcement phases.
