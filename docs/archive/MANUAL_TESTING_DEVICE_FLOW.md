# Manual Testing: Device Authorization Flow

This guide describes how to manually verify the functionality of the Device Authorization Flow using `curl` and your browser.

## Prerequisites
- The **HybridIdP** application must be running (`https://localhost:7035`).

## Step 1: Initiate Device Authorization Request
Use `curl` to send a request to the device endpoint. This simulates a device (like a TV or CLI tool) asking to connect.

```powershell
curl --location 'https://localhost:7035/connect/device' `
--header 'Content-Type: application/x-www-form-urlencoded' `
--data-urlencode 'client_id=testclient-device' `
--data-urlencode 'scope=openid profile offline_access'
```

**Expected Response:**
You should receive a JSON response containing `device_code`, `user_code`, `verification_uri`, etc.
```json
{
  "device_code": "cf83...",
  "user_code": "KBWD-NDSL",
  "verification_uri": "https://localhost:7035/connect/verify",
  "verification_uri_complete": "https://localhost:7035/connect/verify?user_code=KBWD-NDSL",
  "expires_in": 1800,
  "interval": 5
}
```

## Step 2: Poll for Token (Simulate Device Waiting)
The device would normally poll this endpoint. You can run this command repeatedly. Initially, it will return `authorization_pending`.

```powershell
# Replace [DEVICE_CODE] with the code from Step 1
curl --location 'https://localhost:7035/connect/token' `
--header 'Content-Type: application/x-www-form-urlencoded' `
--data-urlencode 'grant_type=urn:ietf:params:oauth:grant-type:device_code' `
--data-urlencode 'client_id=testclient-device' `
--data-urlencode 'device_code=[DEVICE_CODE]'
```

**Expected Response (Before Approval):**
```json
{
  "error": "authorization_pending",
  "error_description": "The authorization request is still pending."
}
```

## Step 3: Approve Authorization (User Action)
1. Open a browser and navigate to the `verification_uri` (e.g., `https://localhost:7035/connect/verify`).
2. Log in if requested (use `testuser` / `Pa$$word123`).
3. Enter the `user_code` from Step 1 (e.g., `KBWD-NDSL`).
4. Click **Submit**.
   - *Note:* In this MVP implementation, clicking Submit automatically approves the request. Using the manual workaround, this grants `openid`, `profile`, and `offline_access` scopes.

## Step 4: Verify Token Issuance
Run the command from **Step 2** again.

**Expected Response (After Approval):**
You should now receive an access token.
```json
{
  "access_token": "eyJhbGciOi...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "openid profile offline_access",
  "refresh_token": "..."
}
```

## Troubleshooting
- **"invalid_client"**: Ensure `testclient-device` exists in the database. Run `scripts/create-testclient-device-mssql.sql` if needed.
- **Timeout**: The codes expire after 30 minutes (1800 seconds). If you wait too long, repeat Step 1.
