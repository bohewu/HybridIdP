# Phase 16: Advanced User Support (Impersonation)

## 1. Overview
Enable authorized Administrators to sign in as any user to reproduce issues and provide direct support, with strict security controls including easy reversion.

## 2. Impersonation Flow ("Login As")

### 2.1 Security Controls
- **Permission**: `Permissions.Users.Impersonate`.
- **Restriction**:
    - Admins CANNOT impersonate other Admins.
- **Audit**: Log event `UserImpersonated` (Actor: Admin, Target: User).

### 2.2 API Design

#### Start Impersonation
- **POST** `/api/admin/users/{userId}/impersonate`
- **Body**: Empty or `{}`.
- **Logic**:
    1. Validate Admin permissions.
    2. Verify target user is eligible (not an Admin).
    3. Issue Authentication Cookie for Target User with special claims:
        - `act`: `{ "sub": "admin_user_id", "name": "admin_username" }` (Actor claim standard).
        - `amr`: `impersonation`.

#### Revert Impersonation ("Switch Back")
- **POST** `/api/account/impersonation/revert`
- **Logic**:
    1. Check current user principal for `act` claim.
    2. If missing, return 400.
    3. Validate that the original Admin user (from `act.sub`) still exists and is active.
    4. **Re-issue Admin Cookie**: valid for the original admin user.
    5. Redirect to Admin Dashboard.

### 2.3 UI Experience

#### Admin Dashboard
1.  **Trigger**: "Login as User" button in User Details.
2.  **Confirmation Modal**:
    - Title: "Confirm Impersonation"
    - Body: "You are about to log in as **{User}**. Proceed?"
    - Action: "Start Impersonation" (Primary Button).

#### Impersonation Session
1.  **Global Warning Banner**: Fixed at top of screen (z-index high).
    - Style: Warning/Danger color (Yellow/Red).
    - Text: "👁️ Viewing as **{TargetUser}**. Account changes will be audited."
    - Action: **"Switch Back to Admin"** button (Calls Revert API).

## 3. Deliverables
- [ ] Backend: `StartImpersonation` endpoint (Permission restricted).
- [ ] Backend: `RevertImpersonation` endpoint with `act` claim verification.
- [ ] Frontend: Simple Confirmation Modal.
- [ ] Frontend: Impersonation Warning Banner with "Switch Back" button.
