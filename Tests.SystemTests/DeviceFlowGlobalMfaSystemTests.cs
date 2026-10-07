using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OtpNet;
using Web.IdP.Helpers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.SystemTests;

// Reuse the disposable PostgreSQL/TestServer host; never start the default connected server fixture.
[Collection(OperationalAdminBootstrapRealHostCollection.CollectionName)]
public sealed class DeviceFlowGlobalMfaSystemTests(OperationalAdminBootstrapRealHostFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeviceRedemption_ShouldHonorPolicyActivatedAfterApproval_AndKeepJsonContract(bool completedMfa)
    {
        await fixture.ResetAsync();
        var account = HostBootstrapRequest.Create();
        await using var factory = await OperationalBootstrapHostFactory.CreateAsync(
            fixture.ConnectionString, account, proxyEnabled: false, knownProxies: null,
            remoteAddress: IPAddress.Parse("203.0.113.78"));
        var clientId = $"device-policy-{Guid.NewGuid():N}";
        string? authenticatorKey = null;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = account.Email, Email = account.Email,
                EmailConfirmed = true, IsActive = true
            };
            Assert.True((await users.CreateAsync(user, account.Password)).Succeeded);
            user.LastPasswordChangeDate = DateTime.UtcNow;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            if (completedMfa)
            {
                Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
                authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
                Assert.False(string.IsNullOrWhiteSpace(authenticatorKey));
                Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            }
            var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            foreach (var name in new[] { Scopes.OpenId, Scopes.Profile })
            {
                if (await scopes.FindByNameAsync(name) is null)
                    await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = name });
            }
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId, ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Implicit,
                DisplayName = "Disposable device policy client",
                Properties = { [AuthConstants.Properties.RequireMfa] = JsonSerializer.SerializeToElement(false) },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.DeviceAuthorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.DeviceCode,
                    OpenIddictConstants.Permissions.Prefixes.Scope + Scopes.OpenId,
                    OpenIddictConstants.Permissions.Scopes.Profile
                }
            });
        }

        using (var discoveryResponse = await factory.Client.GetAsync("/.well-known/openid-configuration"))
        {
            Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
            Assert.Equal("application/json", discoveryResponse.Content.Headers.ContentType?.MediaType);
            using var discovery = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());
            Assert.Equal("/connect/device", new Uri(discovery.RootElement.GetProperty("device_authorization_endpoint").GetString()!).AbsolutePath);
            Assert.Equal("/connect/token", new Uri(discovery.RootElement.GetProperty("token_endpoint").GetString()!).AbsolutePath);
            Assert.Contains(discovery.RootElement.GetProperty("grant_types_supported").EnumerateArray(),
                grant => grant.GetString() == GrantTypes.DeviceCode);
        }

        using (var loginPage = await factory.Client.GetAsync("/Account/Login"))
        {
            Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
            using var loginForm = new FormUrlEncodedContent(
            [
                new("Input.Login", account.Email), new("Input.Password", account.Password),
                new("__RequestVerificationToken", HiddenValue(await loginPage.Content.ReadAsStringAsync(), "__RequestVerificationToken"))
            ]);
            using var login = await factory.Client.PostAsync("/Account/Login", loginForm);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            if (completedMfa)
            {
                Assert.StartsWith("/Account/LoginTotp", login.Headers.Location?.OriginalString);
                using var mfaPage = await factory.Client.GetAsync(login.Headers.Location);
                Assert.Equal(HttpStatusCode.OK, mfaPage.StatusCode);
                using var mfaForm = new FormUrlEncodedContent(
                [
                    new("Input.TotpCode", new Totp(Base32Encoding.ToBytes(authenticatorKey!)).ComputeTotp()),
                    new("__RequestVerificationToken", HiddenValue(await mfaPage.Content.ReadAsStringAsync(), "__RequestVerificationToken"))
                ]);
                using var mfa = await factory.Client.PostAsync("/Account/LoginTotp", mfaForm);
                Assert.Equal(HttpStatusCode.Redirect, mfa.StatusCode);
            }
        }

        using var deviceForm = new FormUrlEncodedContent(
        [new("client_id", clientId), new("scope", "openid profile")]);
        using var deviceResponse = await factory.Client.PostAsync("/connect/device", deviceForm);
        Assert.Equal(HttpStatusCode.OK, deviceResponse.StatusCode);
        Assert.Equal("application/json", deviceResponse.Content.Headers.ContentType?.MediaType);
        using var device = JsonDocument.Parse(await deviceResponse.Content.ReadAsStringAsync());
        var userCode = device.RootElement.GetProperty("user_code").GetString()!;
        var deviceCode = device.RootElement.GetProperty("device_code").GetString()!;
        var verificationUri = device.RootElement.GetProperty("verification_uri").GetString()!;
        Assert.Equal("/connect/verify", new Uri(verificationUri).AbsolutePath);

        using var verificationPage = await factory.Client.GetAsync($"/connect/verify?user_code={Uri.EscapeDataString(userCode)}");
        Assert.Equal(HttpStatusCode.OK, verificationPage.StatusCode);
        var verificationHtml = await verificationPage.Content.ReadAsStringAsync();
        using var approvalForm = new FormUrlEncodedContent(
        [
            new("user_code", userCode),
            new(DeviceVerificationSession.FormFieldName, HiddenValue(verificationHtml, DeviceVerificationSession.FormFieldName)),
            new("__RequestVerificationToken", HiddenValue(verificationHtml, "__RequestVerificationToken"))
        ]);
        using var approval = await factory.Client.PostAsync("/connect/verify", approvalForm);
        Assert.Equal(HttpStatusCode.Redirect, approval.StatusCode);
        Assert.Equal("/connect/verify/success", approval.Headers.Location?.OriginalString);

        // A real approval is persisted before enabling the policy through its normal cache-invalidating update.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISecurityPolicyService>().UpdatePolicyAsync(new SecurityPolicyDto
            {
                MinPasswordLength = 8, RequireUppercase = true, RequireLowercase = true,
                RequireDigit = true, RequireNonAlphanumeric = true, PasswordHistoryCount = 5,
                PasswordExpirationDays = 180, MaxFailedAccessAttempts = 5, LockoutDurationMinutes = 15,
                EnableTotpMfa = true, EnableEmailMfa = true, EnablePasskey = true, MaxPasskeysPerUser = 5,
                EnforceMandatoryMfaEnrollment = true, MfaEnforcementGracePeriodDays = 30
            }, "device-policy-test");
        }
        using var tokenForm = new FormUrlEncodedContent(
        [new("grant_type", GrantTypes.DeviceCode), new("client_id", clientId), new("device_code", deviceCode)]);
        using var redemption = await factory.Client.PostAsync("/connect/token", tokenForm);
        Assert.Equal("application/json", redemption.Content.Headers.ContentType?.MediaType);
        using var tokenResponse = JsonDocument.Parse(await redemption.Content.ReadAsStringAsync());
        if (!completedMfa)
        {
            Assert.Equal(HttpStatusCode.BadRequest, redemption.StatusCode);
            Assert.Equal(Errors.InvalidGrant, tokenResponse.RootElement.GetProperty("error").GetString());
            Assert.Equal("Multi-factor authentication is required for this device authorization.",
                tokenResponse.RootElement.GetProperty("error_description").GetString());
            Assert.False(tokenResponse.RootElement.TryGetProperty("access_token", out _));
            return;
        }
        Assert.Equal(HttpStatusCode.OK, redemption.StatusCode);
        Assert.False(tokenResponse.RootElement.TryGetProperty("error", out _));
        Assert.False(string.IsNullOrWhiteSpace(tokenResponse.RootElement.GetProperty("access_token").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(tokenResponse.RootElement.GetProperty("id_token").GetString()));
    }

    private static string HiddenValue(string html, string name)
    {
        var input = Regex.Match(html, $@"<input\b[^>]*\bname=""{Regex.Escape(name)}""[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(input.Success, $"The page did not contain the {name} form field.");
        var value = Regex.Match(input.Value, @"\bvalue=""([^""]+)""", RegexOptions.IgnoreCase);
        Assert.True(value.Success, $"The {name} form field had no value.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }
}
