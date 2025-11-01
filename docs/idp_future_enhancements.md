# Future Enhancements for HybridAuthIdP

This document outlines features that are important for a robust and secure Identity Provider but can be deferred for implementation in a future release, after the initial core product is delivered.

---

## 1. Content Security Policy (CSP)

**Goal:** Enhance the application's security posture by mitigating Cross-Site Scripting (XSS) and other code injection attacks.

**Description:** Implement a Content Security Policy (CSP) header for the `Web.IdP` application. This policy will define a whitelist of trusted content sources (scripts, stylesheets, images, fonts, etc.) that the browser is allowed to load and execute. Any content from sources not explicitly allowed will be blocked by the browser.

**Implementation Considerations:**
- Start with a strict policy and gradually relax it as needed, using report-only mode initially.
- Identify all legitimate sources for scripts, styles, images, and other assets, including those from third-party libraries (e.g., Cloudflare Turnstile, Vue.js, Vite).
- Configure the CSP header in `Program.cs` or via middleware.

---

## 2. User Email Verification

**Goal:** Improve account security and data quality by ensuring that registered email addresses are valid and owned by the user.

**Description:** Implement an email verification flow for new user registrations. After a user registers, an email containing a unique verification link will be sent to their provided address. The user will not be able to fully log in or use certain features until their email address has been successfully verified by clicking this link.

**Implementation Considerations:**
- Extend the `ApplicationUser` entity with a property like `EmailConfirmed`.
- Generate a unique, time-limited token for email verification.
- Create an email template for the verification link.
- Implement a Razor Page (`/Account/VerifyEmail`) to handle the verification link and confirm the user's email.
- Modify the login flow to check the `EmailConfirmed` status.
