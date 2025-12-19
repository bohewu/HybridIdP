# OIDC AMR Claim & MFA Enforcement Implementation Plan

Implement the `amr` (Authentication Methods References) claim and support forcing MFA during the OIDC authorization flow via `acr_values`.

## 核心邏輯共識 (Consensus)

1.  **Passkey = MFA**: 由於 Passkey 包含硬體持有 (`hwk`) 與使用者驗證 (`user`)，技術上直接視為滿足 MFA 要求。
2.  **二擇一 (XOR)**：保持現有邏輯，完成 Passkey 驗證即不需補 OTP，反之亦然。
3.  **無縫註冊 (Smooth Enrollment)**：在 `MfaSetup` 頁面完成綁定後，立即刷新 Session Cookie 並導回 OIDC 流程，不需重新登入。
4.  **強制啟動 MFA (Mandatory MFA Enrollment)**：
    *   新增系統設定：`EnforceMandatoryMfaEnrollment`。
    *   **對象：所有帳戶**。不論是新建立還是既有帳戶，只要目前的 Security Policy 被啟動，使用者在下一次登入時若偵測到無任何有效的 MFA/Passkey，將強制跳轉至 `MfaSetup` 完成綁定後才可繼續使用系統。
5.  **防止移除最後一項 MFA (Prevent Last MFA Removal)**：
    *   若 `ForceMfaSetupOnFirstLogin` 為真，系統必須禁止使用者移除（Disable/Delete）最後一個有效的驗證因素（TOTP, Email 或最後一個 Passkey）。
    *   此限制必須同時在 **前端 UI**（禁用按鈕）與 **後端 API**（返回 400 錯誤）中實作。

## Proposed Changes

### Core Domain

#### [MODIFY] [AuthConstants.cs](file:///c:/repos/HybridIdP/Core.Domain/Constants/AuthConstants.cs)
- Add constants for `amr` values: `pwd`, `otp`, `mfa`, `hwk`.

#### [MODIFY] [SecurityPolicy.cs](file:///c:/repos/HybridIdP/Core.Domain/Entities/SecurityPolicy.cs)
- Add `EnforceMandatoryMfaEnrollment` boolean property.

### Web IdP

#### [MODIFY] [Login.cshtml.cs](file:///c:/repos/HybridIdP/Web.IdP/Pages/Account/Login.cshtml.cs)
- Add `amr: pwd` claim to the identity when a user signs in with password.
- Check `EnforceMandatoryMfaEnrollment`. If true and user has no MFA, redirect to `MfaSetup`.

#### [MODIFY] [LoginTotp.cshtml.cs](file:///c:/repos/HybridIdP/Web.IdP/Pages/Account/LoginTotp.cshtml.cs)
- Add `amr: ["pwd", "mfa", "otp"]` claims upon success.

#### [NEW] [MfaSetup.cshtml](file:///c:/repos/HybridIdP/Web.IdP/Pages/Account/MfaSetup.cshtml)
- **Interactive Selection**: Display enabled MFA options (TOTP, Email) or Passkey registration based on `SecurityPolicy`.
- **Session Update**: Call `_signInManager.SignInAsync` after setup to update claims.
- **Redirect**: Directly back to `returnUrl` (OIDC authorize endpoint) or Home.

#### [MODIFY] [MfaController.cs](file:///c:/repos/HybridIdP/Web.IdP/Controllers/Account/MfaController.cs)
- **API Enforcement**: In `/disable` and `/email/disable` endpoints, check if `EnforceMandatoryMfaEnrollment` is active.
- If the method being disabled is the only one left, return `BadRequest` with error code `lastMfaExclusionRequired`.

#### [MODIFY] [PasskeyController.cs](file:///c:/repos/HybridIdP/Web.IdP/Controllers/Account/PasskeyController.cs)
- **API Enforcement**: If deleting a passkey, check if it's the last one and no other MFA (TOTP/Email) is active under the mandatory policy.

#### [MODIFY] [AuthorizationService.cs](file:///c:/repos/HybridIdP/Web.IdP/Services/AuthorizationService.cs)
- Check `acr_values=mfa`.
- **Validation**:
    - Match if `amr` contains `mfa` OR `hwk`.
    - If no match:
        - If MFA disabled globally -> Return `unmet_authentication_requirements`.
        - Else If user has no MFA setup -> Redirect to `MfaSetup`.
        - Else -> Challenge via existing MFA pages (LoginMfa / LoginEmailOtp).
- Propagate `amr`/`acr` to tokens.

#### [MODIFY] [AuthorizationService.cs] (GetDestinations)
- Ensure `amr` and `acr` are included in both Access and Identity tokens.

## Technical Details

### AMR Values Reference

| Method | AMR Claim Value | Satisfies MFA Requirement |
| :--- | :--- | :--- |
| Password | `["pwd"]` | No |
| TOTP / Email OTP | `["pwd", "mfa", "otp"]` | Yes |
| Passkey (WebAuthn) | `["hwk", "user"]` | Yes (Per NIST/modern OIDC) |

### OIDC Error Handling

If `acr_values=mfa` is requested but cannot be fulfilled, return:
- **Error**: `unmet_authentication_requirements`.
- **Description**: "Multi-factor authentication is required but not available or supported."

### MfaSetup Flow

1. **Entry**: Redirected from `AuthorizationService` or `Login` with `returnUrl`.
2. **Action**: User selects and completes setup for TOTP, Email MFA, or Passkey.
3. **Internal Refresh**: System calls `SignInAsync` to update the user's session cookie with the new `mfa` or `hwk` claim.
4. **Exit**: Redirect back to `returnUrl`, where `AuthorizationService` will now detect the claim and proceed.

## Verification Plan

### Automated Tests
- System test: Request `acr_values=mfa` with a password-only user -> Verify redirect to `MfaSetup`.
- System test: Request `acr_values=mfa` with a Passkey user -> Verify direct token issuance with `hwk` claim.
- System test: Enable `EnforceMandatoryMfaEnrollment` -> Verify all non-compliant users are forced to `MfaSetup` upon login.
- **Enforcement Test**: Attempt to call API to disable the only MFA method when policy is active -> Verify 400 error.
