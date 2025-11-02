# Cloudflare Turnstile Integration

This document explains how to configure and use Cloudflare Turnstile CAPTCHA protection in the HybridAuthIdP application.

## Overview

Turnstile is integrated into the **Login** and **Register** pages to provide optional CAPTCHA protection against automated attacks and abuse. The integration is designed to gracefully degrade when disabled, ensuring the application remains functional without Turnstile credentials.

## Configuration

Turnstile settings are configured in `appsettings.json`:

```json
{
  "Turnstile": {
    "Enabled": false,
    "SiteKey": "",
    "SecretKey": ""
  }
}
```

### Settings

- **Enabled** (bool): Set to `true` to enable Turnstile CAPTCHA on Login and Register pages. Set to `false` to disable it (default).
- **SiteKey** (string): Your Cloudflare Turnstile site key (public key shown to users).
- **SecretKey** (string): Your Cloudflare Turnstile secret key (used for server-side validation).

### Obtaining Turnstile Keys

1. Sign in to your [Cloudflare dashboard](https://dash.cloudflare.com/).
2. Navigate to **Turnstile** in the sidebar.
3. Click **Add a Site**.
4. Configure your site:
   - **Site Name**: e.g., "HybridAuthIdP"
   - **Domain**: `localhost` for development, or your production domain.
   - **Widget Mode**: Choose "Managed" (recommended) or "Non-interactive".
5. Click **Create**.
6. Copy the **Site Key** and **Secret Key** and add them to your `appsettings.json` or environment variables.

### Example Configuration (Enabled)

```json
{
  "Turnstile": {
    "Enabled": true,
    "SiteKey": "1x00000000000000000000AA",
    "SecretKey": "1x0000000000000000000000000000000AA"
  }
}
```

## How It Works

### When Enabled

1. **Login and Register pages** display the Turnstile widget below the form fields.
2. Users must complete the CAPTCHA challenge before submitting the form.
3. On form submission:
   - The client sends a Turnstile response token to the server.
   - The server validates the token with Cloudflare's API using `ITurnstileService`.
   - If validation fails, the form is rejected with an error message.

### When Disabled

- The Turnstile widget is **not rendered** on the Login and Register pages.
- The Turnstile script is **not loaded**.
- Server-side validation is **skipped**, and form submissions proceed normally.

## Service Implementation

The Turnstile validation service is implemented in:

- **Interface**: `Core.Application/ITurnstileService.cs`
- **Implementation**: `Infrastructure/TurnstileService.cs`

The service is registered in `Web.IdP/Program.cs`:

```csharp
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITurnstileService, TurnstileService>();
```

### Validation Logic

The `TurnstileService.ValidateTokenAsync` method:

1. Checks if Turnstile is enabled; if not, returns `true` (pass).
2. Posts the Turnstile response token and optional remote IP to `https://challenges.cloudflare.com/turnstile/v0/siteverify`.
3. Parses the JSON response and returns the validation result.

## Testing

### Test with Turnstile Disabled (Default)

1. Ensure `"Turnstile:Enabled": false` in `appsettings.json`.
2. Start the IdP: `dotnet run --launch-profile https`
3. Navigate to `https://localhost:7035/Account/Login` or `/Account/Register`.
4. Verify the Turnstile widget does **not** appear.
5. Submit the form; it should work normally without CAPTCHA validation.

### Test with Turnstile Enabled

1. Obtain Turnstile keys from Cloudflare (see above).
2. Update `appsettings.json`:

   ```json
   {
     "Turnstile": {
       "Enabled": true,
       "SiteKey": "your-site-key",
       "SecretKey": "your-secret-key"
     }
   }
   ```

3. Start the IdP: `dotnet run --launch-profile https`
4. Navigate to `https://localhost:7035/Account/Login` or `/Account/Register`.
5. Verify the Turnstile widget **appears** before the submit button.
6. Complete the CAPTCHA and submit the form.
7. If you skip the CAPTCHA or it fails, you should see an error message.

## Production Considerations

- **Secure Secrets**: Do not commit `SiteKey` and `SecretKey` to version control. Use environment variables or a secret management service (e.g., Azure Key Vault, AWS Secrets Manager).
- **Domain Whitelist**: Ensure your production domain is added to the Turnstile site configuration in Cloudflare.
- **Rate Limiting**: Turnstile helps prevent abuse, but consider additional rate limiting on your server.

## Future Enhancements

- Add Turnstile to other sensitive forms (e.g., password reset, account recovery).
- Support for Turnstile's "invisible" mode for a seamless user experience.
- Logging and monitoring of Turnstile validation failures for security insights.
