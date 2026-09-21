# Native Forgotten-Password Recovery

## Implemented boundaries

Native password recovery and configurable recovery guidance are implemented behind
deployment controls. The external route remains the default. Native recovery,
bound-directory recovery, pending directory settlement, provider metadata refresh,
and optional password synchronization each require their own explicit enablement.
The one-time credential-migration ceremony remains a separate workflow.

The current implementation preserves these boundaries:

- Recovery email is a separate security record. Contact email alone is not proof.
- Recovery challenges are purpose-, account-, and browser-bound, expiring,
  attempt-limited, and single-use. Responses preserve account-enumeration resistance.
- Local user and Person eligibility remain mandatory independently of provider
  authentication or metadata success.
- Directory recovery requires an exact durable binding, eligible managed state,
  guarded write ordering, independent verification, and local finalization.
  An uncertain write creates a barrier; it must not cause an automatic retry.
- Administrator recovery assistance requires current actor authorization and
  strong authentication. Assistance, temporary credentials, and ordinary recovery
  remain distinct operations.
- Optional secondary password synchronization is disabled by default and remains
  independent from proof and metadata. It preserves proof and target binding,
  replay and expiry controls, and no unsafe retry after an unknown commit.
- Directory credentials and password policy remain directory-owned. HybridIdP
  retains local eligibility, MFA, sessions, and token-issuance authority.

See [Authentication Integration](../AUTHENTICATION_INTEGRATION.md),
[Security](../SECURITY.md), and [Testing](../TESTING.md) for the adjacent
boundaries. Database changes use the documented operator-controlled migration
procedure. Local implementation and tests do not establish deployment or
connected-environment acceptance.

### Configurable neutral recovery guidance (HIDP-16)

The native recovery page now has seven optional settings on the existing
`ForgotPasswordRecovery` configuration surface. Every setting defaults to an
empty string, preserving an OSS deployment with no institution-specific content
and no empty custom-guidance containers:

```json
{
  "ForgotPasswordRecovery": {
    "TopNotice": "@Recovery.Guidance.Top",
    "VerificationTip": "Check your junk mail folder if the code has not arrived.",
    "ResetTip": "@Recovery.Guidance.Reset",
    "SuccessReminder": "Use your new password the next time you sign in.",
    "SupportText": "@Recovery.Guidance.Support",
    "SupportLabel": "Recovery help",
    "SupportUrl": "https://help.example.org/account-recovery"
  }
}
```

`TopNotice`, `VerificationTip`, `ResetTip`, `SuccessReminder`, `SupportText`,
and `SupportLabel` accept literal plain text or `@ResourceKey`. The key portion
is trimmed. Resource lookup selects the first enabled exact-culture row, then an
enabled `en-US` row, then hides the slot. Thus a disabled or missing exact row
may fall back to enabled `en-US`; a disabled or missing fallback hides the slot.
A blank setting, whitespace-only setting, blank key, missing Resource, or
whitespace-only resolved value also hides the slot. An enabled exact-culture row
whose value is whitespace resolves to hidden and does not continue to the
`en-US` fallback. Clearing any setting and restarting hides its slot.

Resource-backed text is resolved for every request, so Resource updates become
visible on the next request. Configuration remains bound through the existing
startup-lifecycle `IOptions<ForgotPasswordRecoveryOptions>` registration; no
configuration hot-reload guarantee is made.

`SupportUrl` is configured independently and is not localized. It is eligible
only when it parses as an absolute `http` or `https` URL without UserInfo. Even
then, a link requires a nonblank resolved `SupportLabel`. `SupportText` is
independent and remains renderable when the URL or label does not qualify.

The page keeps the following stable, neutral hierarchy:

| Phase | Guidance placement |
| --- | --- |
| Start | Applicable core validation first, then `TopNotice`, then the identifier form. |
| Awaiting code | Core sent/error content first, then `TopNotice`, then `VerificationTip`, then the verification form. |
| Awaiting password | Core password prompt/error content first, then `TopNotice`, then `ResetTip`, then the password form and authoritative policy content. |
| Recovery succeeded | Core success content first, then `TopNotice`, then `SuccessReminder`, then the sign-in action. |
| Every native phase | Independently resolved support text and any eligible support link appear near the bottom after the phase form or action. |

Only the phase-specific tip shown in the table is eligible. In particular,
`SuccessReminder` is absent from validation, rejected-password, failed-
verification, denied-reset, unavailable, and uncertain outcomes. All configured
text is rendered as Razor-encoded plain text; markup is not executed and no
`Html.Raw` path exists. Custom content remains visually secondary to core sent,
error, password-policy, and success content.

Presentation is identical for a fixed culture and phase regardless of account
existence, eligibility, recovery email, or source-cohort classification. This
feature adds no audience targeting and changes no native availability or
routing, OTP behavior, directory/AD write, session, token, or `PasswordHash`
behavior.

## Verification status

HIDP-16 guidance is an existing completed feature. Its product implementation,
locale behavior, and focused tests are preserved. Connected recovery acceptance
and rollout are separate gates; this document does not assert that they passed.
