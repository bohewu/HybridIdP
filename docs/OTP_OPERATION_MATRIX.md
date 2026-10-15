# Email OTP and TOTP operation matrix

Use this matrix for account-security browser regression checks. Record the
result of each row separately; do not infer a complete journey from an API or
unit-test pass. Passkeys, recovery-email enrollment, audit pages and OAuth
client configuration are outside this matrix.

## Fixture and evidence

- Use an isolated test database, a local-password test account with a confirmed
  email address, and Mailpit. Preserve accounts used for human review.
- Enable Email OTP and TOTP in policy. The primary browser matrix uses optional
  MFA, so removing the final OTP method is permitted.
- Use an actual browser and actual SMTP delivery to Mailpit. Do not mock OTP
  endpoints, update enrollment flags in SQL, or alter browser clocks to claim a
  real-browser pass.
- Record the source revision plus uncommitted changes, browser, policy, starting
  factor state, steps, observed result and evidence type.
- Keep passwords, authenticator secrets, QR codes, OTPs, recovery codes, cookies
  and unredacted traces out of reports and Git. Mask enrollment screenshots.
- Observe the current page after each navigation. Reauthentication can use an
  existing OTP directly; it does not always require another password page.
- Record a tool interruption as incomplete unless the remaining assertions are
  independently observed. A database mutation alone does not prove the browser
  retained its session or returned to the intended page.

State notation: `none`, `email`, `totp`, and `both` describe enabled OTP methods.
Do not count a retained but disabled authenticator seed as an enabled method.

## Email enrollment

| ID | Start | Actions | Required observation |
| --- | --- | --- | --- |
| E01 | none | Profile: enable Email OTP; cancel the identity notice. | Purpose is clear; remain on Profile; no mail sent or factor enabled. |
| E02 | none | Enable Email OTP; continue; authenticate. | Authenticate first, then return directly to the email enrollment form. |
| E03 | email form | Enter six digits before sending; send; try five and six digits. | No pending challenge means submit disabled; after sending, five disabled and six enabled; localized placeholder present. |
| E04 | email form | Send a code; inspect Mailpit and resend control. | One new message reaches the intended test recipient; cooldown prevents immediate resend. |
| E05 | pending email code | Submit an incorrect six-digit code. | Localized error, enrollment remains off, form allows a retry while challenge remains valid. |
| E06 | pending email code | Wait for cooldown; resend; submit the new code. | New code enables Email OTP and updates Profile without another enable click. |
| E07 | pending email code | Submit five incorrect codes. | Challenge becomes unavailable; six digits no longer enable submit; UI explains how to obtain a new code. |
| E08 | exhausted email code | Wait for cooldown; send again; try the retired code, then the new one. | Retired code rejected; new challenge restores the form and can complete enrollment. |
| E09 | totp | Enable Email OTP; complete TOTP reauthentication; send and verify email code. | Purpose notice precedes reauthentication; email enrollment follows it; both factors enabled. |
| E10 | valid unused email code | Wait for cooldown; resend before consuming old code; try old, then new. | Old code rejected even though it was never consumed; new code completes the original operation. |

## Authenticator enrollment

| ID | Start | Actions | Required observation |
| --- | --- | --- | --- |
| T01 | email | Add authenticator; cancel the identity notice. | Clear purpose; remain on Profile; no authenticator enabled. |
| T02 | email | Add authenticator; verify with Email OTP. | Six-digit submit works; original purpose remains visible; return directly to authenticator setup. |
| T03 | authenticator form | Cancel setup, then return to account. | Normal Profile response, no 403, no authenticator enabled. |
| T04 | authenticator form | Try five digits and a code from an expired time window. | Short input disabled; expired code rejected; form remains usable. |
| T05 | authenticator form | Submit valid TOTP; save any first-enrollment backup codes; finish. | Authenticator enabled; genuine first enrollment shows ten account backup codes once, while accounts with factor or batch history receive no automatic replacement batch; completion returns to Profile. |
| T06 | both removed | Add authenticator again; authenticate; try old seed's code, then new seed's code. | No Dashboard detour; rotated seed differs, old seed rejected, new seed can enroll. |

## Login and switching methods

| ID | Start | Actions | Required observation |
| --- | --- | --- | --- |
| L01 | email, signed out | Password login; enter six digits before sending, then send. | Cannot submit without a live challenge; sending starts cooldown. |
| L02 | email challenge | Submit wrong code, then correct code. | Error is recoverable; six-digit button enabled; successful login reaches intended return page. |
| L03 | totp, signed out | Password login; submit expired TOTP, then current TOTP. | Wrong window rejected; valid code reaches intended return page. |
| L04 | just used TOTP | Immediately reuse the same time-window code for a new security operation. | Replay rejected; a code from the next valid window can continue. |
| L05 | both | During reauthentication switch TOTP to Email, back to TOTP, and back to Email. | Original operation and return path survive; Email challenge/cooldown state remains accurate. |
| L06 | email login just completed | Immediately add TOTP; enter six digits before sending, then obtain a new code and enroll. | Consumed challenge cannot be reused; new code works; complete setup returns to Profile. |

## Removing methods

| ID | Start | Actions | Required observation |
| --- | --- | --- | --- |
| D01 | both | Disable Email OTP; cancel purpose notice. | Email OTP remains enabled. |
| D02 | both | Disable Email OTP; try expired TOTP, then valid TOTP. | Reauthentication first; automatic completion on return; Email off, TOTP on, current session still usable. |
| D03 | pre-removal session copy | After a successful removal, request authenticated data using current and old sessions separately. | Current authorized session works; old security-stamp session rejected. Never print cookies. |
| D04 | totp confirmation | Inspect password placeholder; cancel removal confirmation. | Placeholder localized; cancel preserves TOTP. |
| D05 | totp | Disable TOTP; proceed through purpose notice and OTP reauthentication. | Reauthentication precedes final password/code confirmation; no entered password is discarded by a redirect. |
| D06 | totp confirmation | Submit empty or wrong password, then correct password. | Empty/wrong rejected with localized errors; factor unchanged until successful confirmation. |
| D07 | totp only | Complete removal, then read authenticated state. | Both OTP factors off; OTP-dependent recovery codes are no longer usable, independent account backup codes remain available, and the current session is retained; no false login-expired card. |
| D08 | both | Disable TOTP using Email OTP reauthentication, then final password confirmation; refresh. | TOTP off, Email on, current session still valid after refresh. |
| D09 | email only | Disable Email OTP using a new Email OTP challenge; refresh. | Automatically complete original removal on return; no second disable click; current session remains valid. |

## Authenticator recovery codes

| ID | Start | Actions | Required observation |
| --- | --- | --- | --- |
| R01 | totp | Regenerate recovery codes; cancel purpose notice. | Notice precedes credential entry; cancel preserves codes. |
| R02 | totp | Regenerate; reauthenticate; try empty/wrong confirmation, then correct. | Confirmation follows reauthentication; empty/wrong rejected; ten new codes returned. |
| R03 | regenerated codes | Sign out; password login; try a retired recovery code, then a new one. | Retired code rejected; new code logs in and decreases remaining count. |
| R04 | consumed recovery code | Sign out and try that successful code again. | Consumed code rejected; another valid method can still sign in. |

## Separate policy and failure evidence

These require different fixtures or controlled failure/time injection. Report
their evidence separately from the local-password, optional-MFA browser matrix.

| Boundary | Existing focused test surface | Browser fixture needed for an additional browser claim |
| --- | --- | --- |
| Email challenge expiry, absent expiry, exhaustion and persistence failure | `MfaServiceTests`, `LoginEmailOtpModelTests`, `MfaControllerRecoveryCodesTests` | An actually expired challenge or an explicitly declared injected clock/failure. |
| Reauthentication proof expired, consumed, wrong user or revoked stamp | `MfaEnrollmentSessionTests`, `AccountSecurityOperationSessionTests` | Independently expired/revoked session; do not infer from an ordinary successful login. |
| Mandatory MFA and last-factor removal | `MfaServiceTests` | Separate account/policy; final required factor must not be removable. |
| No local password | `MfaControllerFactorRemovalTests`, `MfaControllerRecoveryCodesTests` | Authorized external/passwordless test account and its actual sign-in method. |
| Concurrent stamp change or bearer caller | `MfaControllerFactorRemovalTests`, `PasskeyControllerTests` | Distinct concurrent/revoked session or bearer caller; no cookie issuance or adoption of unrelated authority. |
| SMTP unavailable or delayed | Delivery failure and UI error handling tests where available | Isolated mail service failure; never stop the mail service used by an active human review. |

Store execution reports and screenshots outside tracked source. A report must
identify browser passes, focused automated passes, incomplete cases and cases
not executed, rather than displaying one blanket pass for this document.
