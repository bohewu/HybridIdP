# User Impersonation

## Overview
User Impersonation allows authorized Administrators to sign in as any user to reproduce issues and provide direct support. This feature is controlled by strict permissions and audit logging.

## Usage

### 1. Start Impersonation
1.  Navigate to **Users** in the Admin Dashboard.
2.  Find the target user in the list.
3.  Click the **Actions** menu (three dots) on the right.
4.  Select **Login As**.
5.  Based on your configuration, you may see a confirmation dialog.

### 2. During Impersonation
Once impersonation starts:
- You will be logged in as the target user.
- A **Warning Banner** will appear at the top of the screen: "You are currently impersonating {User}".
- All actions performed will be logged with the `Actor` claim pointing to your Admin account.

### 3. Stop Impersonation
1.  Click the **Switch Back** button in the warning banner.
2.  You will be logged out of the target user session and returned to your Admin session.

## Architecture & Security

### Permissions
- Required Permission: `Permissions.Users.Impersonate`
- Administrators cannot impersonate other Administrators to prevent privilege escalation or audit confusion.

### Authentication
- Uses the `act` (Actor) claim in the User Principal / Cookie.
- **Start**: The system issues a new cookie for the target user, embedding the Admin's identity in the `Identity.Actor` property.
- **Stop**: The system validates the `Actor` claim, ensures the original Admin account is still active, and re-issues the Admin cookie.

### Audit Logging
- All critical actions (User Creation, Updates, Deletion) automatically record the `ImpersonatorId` if present in the claims.
- The `ImpersonationService` handles the logic for safely switching contexts.

## Configuration
- This feature is enabled by default for users with the `Users.Impersonate` permission.
- The **Warning Banner** is a global component in the Admin Layout.
