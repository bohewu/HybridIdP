# Web demo client

Run the IdP and this client from the repository root:

```powershell
dotnet run --project Web.IdP
dotnet run --project samples/TestClient
```

Open `https://localhost:7001`. Trust the ASP.NET Core development certificate
with `dotnet dev-certs https --trust` if needed. Certificate checks remain enabled.

The development seeder registers `testclient-public` with
`https://localhost:7001/signin-oidc` and
`https://localhost:7001/signout-callback-oidc`. The client is public and uses
Authorization Code + PKCE with PAR for normal requests. It has no client secret.

## Demo journey

1. Open the demo center and choose standard sign-in.
2. Authenticate at the IdP using an enabled password, external provider or Passkey.
3. Approve access. Inspect the validated claims, authentication methods,
   authentication time, token availability and access-token expiry.
4. Call UserInfo. The returned profile reflects the granted scopes.
5. Refresh tokens and call UserInfo again. Repeat to demonstrate rotation:
   replacement access/refresh tokens and expiry are saved back into the
   protected client cookie before the API call. The session page shows the
   successful refresh count. The validated identity and ID token are retained.
6. End the session and confirm logout at the IdP.

Additional scenario cards request `acr_values=mfa`, `prompt=login`,
`max_age=0`, `prompt=consent`, `prompt=none`, and an intentionally invalid scope.
The invalid-scope scenario sends a PKCE authorization request to `/connect/par`
and displays the actual HTTP 400 JSON `invalid_scope` response code. It does not
start sign-in: rejected authorize requests can remain on the IdP instead of
returning through a client callback. Silent sign-in can return `login_required`, `consent_required` or
`interaction_required`; these are expected protocol outcomes.

The account links open the IdP's existing registration, recovery and profile
pages. Passkey enrollment, TOTP/email MFA, recovery and external-provider options
follow IdP policy and deployment configuration. This client does not enable those
features or call a particular credential-proof producer.

For local TOTP demonstrations the development seeder includes
`amr-mfa@hybridauth.local`, password `Test@123`, and the fixture authenticator key
documented in `Infrastructure/Seeding/UserSeeder.cs`. Use an authenticator app
with that fixture key. These are public development fixtures, never production
credentials. An actual Passkey demonstration requires enrolling a key at the IdP.
The Passkey card requests fresh sign-in so an existing SSO session does not skip
the IdP's credential chooser. Enrollment still follows the IdP's step-up policy.
Email OTP and password recovery require working mail and the relevant IdP policy.

## Configuration

The `Oidc` section configures `Authority`, `ClientId` and `Scopes`.
Environment overrides include `Oidc__Authority` and `Oidc__ClientId`.
The authority must use HTTPS. Register the exact callback/logout URIs and allowed
scopes at the destination IdP before using a different deployment.

Refresh and logout use antiforgery-protected POST forms. Account responses use
`Cache-Control: no-store`. The UI shows token status instead of raw token values
and does not put credentials in browser storage. Tokens are saved in the
encrypted, HttpOnly, Secure authentication cookie for this local sample; a
production client should review token/session storage and cookie-size limits.
Refresh failure shows a bounded error without raw endpoint bodies; uncertain
send outcomes require a new sign-in rather than an automatic retry.

## Other samples

The demo center also displays these commands:

```powershell
dotnet run --project samples/TestClient.Device -- --no-browser
dotnet run --project samples/TestClient.M2M
dotnet run --project samples/TestClient.Impersonation
```

These remain separate console applications. See `samples/README.md` for their
development credentials and the privileged impersonation fixture requirement.
