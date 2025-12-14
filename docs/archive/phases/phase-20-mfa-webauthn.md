# Phase 20 — MFA & WebAuthn

**Status**: 0%
**Goal**: Implement Multi-Factor Authentication with TOTP and WebAuthn Passkey support.

---

## Sub-Phases

### Phase 20.1: TOTP (Time-based One-Time Password)
**Complexity**: ⭐⭐ Medium | **Estimate**: 2-3 days

- [ ] Add `TwoFactorEnabled`, `AuthenticatorKey` fields to ApplicationUser
- [ ] API: `POST /api/account/2fa/setup` - Generate TOTP secret + QR code
- [ ] API: `POST /api/account/2fa/verify` - Verify TOTP and enable 2FA
- [ ] API: `POST /api/account/2fa/disable` - Disable 2FA
- [ ] Login flow integration: Prompt for TOTP after password verification
- [ ] Recovery codes generation and storage
- [ ] Admin UI: View user 2FA status, force reset
- [ ] Unit tests + E2E tests

### Phase 20.2: WebAuthn Passkey - Database & Backend
**Complexity**: ⭐⭐⭐ Medium-High | **Estimate**: 2-3 days

- [ ] Install `Fido2.AspNet` NuGet package
- [ ] Create `UserCredential` entity (CredentialId, PublicKey, SignCount, DeviceName)
- [ ] EF Core migration for UserCredentials table
- [ ] Fido2 configuration in DI (RelyingPartyId, Origins)
- [ ] API: `POST /api/passkey/register-options` - Generate registration challenge
- [ ] API: `POST /api/passkey/register` - Store credential after browser attestation
- [ ] API: `POST /api/passkey/login-options` - Generate authentication challenge
- [ ] API: `POST /api/passkey/login` - Verify signature + SignIn
- [ ] Integrate with `ValidatePersonStatusAsync()` for Person Lifecycle check

### Phase 20.3: WebAuthn Passkey - Frontend UI
**Complexity**: ⭐⭐ Medium | **Estimate**: 1-2 days

- [ ] User Settings page: "Manage Passkeys" section
- [ ] "Add Passkey" button with WebAuthn registration flow
- [ ] List registered passkeys with device names
- [ ] Delete passkey functionality
- [ ] Login page: "Sign in with Passkey" option
- [ ] JavaScript integration with `navigator.credentials` API

### Phase 20.4: Testing & Documentation
**Complexity**: ⭐ Low | **Estimate**: 1 day

- [ ] Unit tests for TOTP validation
- [ ] Integration tests for Fido2 flows (using virtual authenticators)
- [ ] E2E tests with Playwright (using Chrome's Virtual Authenticator)
- [ ] Update `SECURITY.md` with MFA documentation
- [ ] User guide for setting up Passkeys

---

## WebAuthn: How It Works for Users

### Development/Testing Environment

**Chrome DevTools Virtual Authenticator**:
1. Open DevTools → More Tools → WebAuthn
2. Enable "Virtual Authenticator Environment"
3. Add authenticator (supports ctap2, internal, userVerification)
4. Test registration/login without physical device!

**Playwright E2E Testing**:
```javascript
// Create virtual authenticator
await page.context().addCDPSession(page);
const client = await page.context().newCDPSession(page);
await client.send('WebAuthn.enable');
await client.send('WebAuthn.addVirtualAuthenticator', {
  options: { protocol: 'ctap2', transport: 'internal', hasUserVerification: true }
});
```

### Production User Flow

1. **User opens login page** on `https://auth.company.com`
2. **Clicks "Sign in with Passkey"**
3. **Browser prompts** for biometric (Windows Hello / Touch ID / Face ID)
4. **User verifies** using fingerprint/face/PIN
5. **Browser sends signed challenge** to server
6. **Server validates** signature + Person Lifecycle check
7. **User logged in** ✅

### Supported Platforms (No App Required)

| Platform | Authenticator |
|----------|--------------|
| Windows 10/11 | Windows Hello (PIN/Fingerprint/Face) |
| macOS | Touch ID on MacBook / iCloud Keychain |
| iOS | Face ID / Touch ID + Safari |
| Android | Fingerprint / Face unlock + Chrome |
| Cross-device | Bluetooth/QR scan from phone to laptop |

---

## Dependencies

- [Fido2.AspNet](https://github.com/passwordless-lib/fido2-net-lib) - .NET WebAuthn implementation
- [OtpNet](https://github.com/kspearrin/Otp.NET) - TOTP generation/validation (optional, ASP.NET Identity has built-in)

---

## Notes

- WebAuthn requires **HTTPS** in production (localhost works for dev)
- Passkeys can be synced across devices via iCloud/Google Password Manager
- Pre-provisioned model: User must have existing account before registering Passkey
