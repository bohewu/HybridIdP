# Future Enhancement: Multi-Factor Authentication (MFA)

This document outlines the requirements for implementing Multi-Factor Authentication (MFA) in the HybridAuthIdP project. This feature is planned for a future release and is not part of the initial implementation.

---

## Phase X: Multi-Factor Authentication

**Goal:** Enhance user security by adding a second factor of authentication. The initial implementation will focus on Time-Based One-Time Passwords (TOTP).

**Definition of Done:**
- Users can enable and disable MFA on their account via a self-service portal.
- When MFA is enabled, the login flow requires the user to enter a TOTP after password verification.
- The IdP provides a mechanism for users to recover their account if they lose their MFA device.

### Steps:

1.  **Extend User Entity and DbContext:**
    - Add properties to `ApplicationUser` in `Core.Domain` to support MFA:
        ```csharp
        public bool TwoFactorEnabled { get; set; }
        public string TwoFactorSecretKey { get; set; } // Encrypted
        // Add fields for recovery codes
        ```
    - Update `ApplicationDbContext` and create a new database migration.

2.  **Implement TOTP Logic:**
    - Add a service (e.g., `ITotpService`) in `Core.Application` and its implementation in `Infrastructure`.
    - This service will be responsible for:
        - Generating a new secret key for a user.
        - Generating a QR code URI (e.g., `otpauth://totp/...`).
        - Validating a user-provided TOTP code against the secret key.

3.  **Create User Self-Service UI for MFA Management:**
    - In the "User Account Management" portal (from Phase 6), add a new section for MFA.
    - **Enable MFA Flow:**
        1. User clicks "Enable MFA."
        2. The system generates a secret key and displays it as a QR code and a manual entry key.
        3. The user scans the QR code with an authenticator app (e.g., Google Authenticator, Authy).
        4. The user enters a TOTP from their app to verify and enable MFA.
        5. The system presents a set of single-use recovery codes to the user.
    - **Disable MFA Flow:**
        1. User must authenticate (potentially with a TOTP code) to disable MFA.

4.  **Integrate MFA into the Login Flow:**
    - In `Web.IdP/Pages/Account/Login.cshtml.cs`, after a successful password sign-in, check if the user has MFA enabled.
    - If MFA is enabled, redirect the user to a new `LoginWith2fa.cshtml` page to enter their TOTP code.
    - Use `SignInManager.TwoFactorSignInAsync()` to complete the login.

5.  **Implement Account Recovery:**
    - Create a flow for users to use their recovery codes if they lose access to their MFA device.

### Agent Verification for Phase X:

- **Action:** Pause execution.
- **Question:** "Phase X (Multi-Factor Authentication) is complete. Users can now enroll in TOTP-based MFA, log in using a second factor, and recover their accounts. **Are there any further tasks?**"
