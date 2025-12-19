# Phase 20.4 - WebAuthn Passkey UI Implementation

## Progress

### Step 0: SecurityPolicy - MFA Feature Toggles ✅
- [x] 0.1 Update SecurityPolicy Entity (adding EnableTotpMfa, EnableEmailMfa, EnablePasskey, MaxPasskeysPerUser)
- [x] 0.2 Update SecurityPolicyDto
- [x] 0.3 Create Migrations (SQL Server + PostgreSQL)
- [x] 0.4 Apply Migrations
- [x] **COMMITTED**: `feat: Add MFA feature toggles to SecurityPolicy (Step 0)`

### [x] **Step 1: Backend - Security Fixes & New APIs (P1)**
    - [x] ~~Implement actual Fido2 in `PasskeyService` (Register/Verfiy)~~
    - [x] ~~Add `Person.Status` check in `PasskeyController`~~
    - [x] ~~Implement `MaxPasskeysPerUser` check~~
    - [x] ~~Implement List Passkeys API~~
    - [x] ~~Implement Delete Passkey API~~
[ ] **Step 2: Frontend - `useWebAuthn` Composable (P2)**
    - [ ] Create `useWebAuthn.js` to handle FIDO2 API interaction
    - [ ] Implement `base64url` conversion utilities/useWebAuthn.js`

### Step 3: Frontend - MfaSettings.vue Integration
- [ ] 3.1 Add Passkey section to MfaSettings.vue

### Step 4: i18n Translations
- [ ] 4.1 Add English translations (en-US/mfa.json)
- [ ] 4.2 Add Chinese translations (zh-TW/mfa.json)

### Step 5: Admin UI - Security Settings
- [ ] 5.1 Add MFA feature toggles to Admin Security Settings

### Verification
- [ ] Build verification
- [ ] Manual testing
