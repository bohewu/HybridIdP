using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Web.IdP.Helpers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.SystemTests;

[Collection(OperationalAdminBootstrapRealHostCollection.CollectionName)]
public sealed class DeviceFlowSecuritySystemTests(OperationalAdminBootstrapRealHostFixture fixture)
{
    [Theory]
    [InlineData(ConsentTypes.Explicit, "accept")]
    [InlineData(ConsentTypes.Systematic, "deny")]
    [InlineData(ConsentTypes.Implicit, "accept")]
    public async Task ManualVerification_ShouldRequireReviewAndDecision_ThenHonorApprovalOrDenial(string consentType, string decision)
    {
        await fixture.ResetAsync();
        var account = HostBootstrapRequest.Create();
        await using var factory = await OperationalBootstrapHostFactory.CreateAsync(
            fixture.ConnectionString, account, false, null, IPAddress.Parse("203.0.113.79"));
        var clientId = await RegisterClientAsync(factory, consentType);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = account.Email, Email = account.Email, EmailConfirmed = true, IsActive = true };
            Assert.True((await users.CreateAsync(user, account.Password)).Succeeded);
            user.LastPasswordChangeDate = DateTime.UtcNow;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }
        using var loginPage = await factory.Client.GetAsync("/Account/Login");
        using var loginForm = new FormUrlEncodedContent([
            new("Input.Login", account.Email), new("Input.Password", account.Password),
            new("__RequestVerificationToken", HiddenValue(await loginPage.Content.ReadAsStringAsync(), "__RequestVerificationToken"))]);
        using var login = await factory.Client.PostAsync("/Account/Login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var deviceForm = new FormUrlEncodedContent([new("client_id", clientId), new("scope", "openid profile")]);
        using var deviceResponse = await factory.Client.PostAsync("/connect/device", deviceForm);
        Assert.Equal(HttpStatusCode.OK, deviceResponse.StatusCode);
        using var device = JsonDocument.Parse(await deviceResponse.Content.ReadAsStringAsync());
        var userCode = device.RootElement.GetProperty("user_code").GetString()!;
        var deviceCode = device.RootElement.GetProperty("device_code").GetString()!;

        using var manualPage = await factory.Client.GetAsync("/connect/verify");
        var manualHtml = await manualPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain("device-consent-scopes", manualHtml);
        using var forgedDecision = await PostVerificationAsync(factory.Client, manualHtml, userCode, "accept");
        Assert.Equal(HttpStatusCode.BadRequest, forgedDecision.StatusCode);
        await AssertPollingErrorAsync(factory.Client, clientId, deviceCode, Errors.AuthorizationPending);

        using var freshManualPage = await factory.Client.GetAsync("/connect/verify");
        using var review = await PostVerificationAsync(factory.Client,
            await freshManualPage.Content.ReadAsStringAsync(), userCode, "continue");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewHtml = await review.Content.ReadAsStringAsync();
        Assert.Contains("Disposable device security client", reviewHtml);
        Assert.Contains("device-consent-scopes", reviewHtml);
        Assert.Contains("<code>openid</code>", reviewHtml);
        Assert.Contains("<code>profile</code>", reviewHtml);
        Assert.Contains("value=\"accept\"", reviewHtml);
        Assert.Contains("value=\"deny\"", reviewHtml);
        Assert.Contains("readonly", reviewHtml);

        // A resolved code still cannot be approved by an absent decision.
        using var missingDecision = await PostVerificationAsync(factory.Client, reviewHtml, userCode, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingDecision.StatusCode);
        var retryHtml = await missingDecision.Content.ReadAsStringAsync();
        using var result = await PostVerificationAsync(factory.Client, retryHtml, userCode, decision);
        if (decision == "accept")
        {
            Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
            Assert.Equal("/connect/verify/success", result.Headers.Location?.OriginalString);
            using var redemption = await PollAsync(factory.Client, clientId, deviceCode);
            Assert.Equal(HttpStatusCode.OK, redemption.StatusCode);
            Assert.Equal("application/json", redemption.Content.Headers.ContentType?.MediaType);
            using var token = JsonDocument.Parse(await redemption.Content.ReadAsStringAsync());
            Assert.False(string.IsNullOrWhiteSpace(token.RootElement.GetProperty("access_token").GetString()));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            await AssertPollingErrorAsync(factory.Client, clientId, deviceCode, Errors.AccessDenied);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeDeviceEndpoints_ShouldEnforceSourceBudgetBeforeOpenIddict_OnlyWhenEnabled(bool enabled)
    {
        await fixture.ResetAsync();
        await using var factory = await OperationalBootstrapHostFactory.CreateAsync(
            fixture.ConnectionString, HostBootstrapRequest.Create(), false, null,
            IPAddress.Parse("203.0.113.80"), rateLimitingEnabled: enabled);
        var clientId = await RegisterClientAsync(factory, ConsentTypes.Implicit);
        using var validForm = new FormUrlEncodedContent([new("client_id", clientId), new("scope", "openid profile")]);
        using var first = await factory.Client.PostAsync("/connect/device", validForm);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        for (var index = 0; index < 3; index++)
        {
            using var invalidForm = new FormUrlEncodedContent([new("client_id", $"untrusted-{index}")]);
            using var response = await factory.Client.PostAsync(index == 0 ? "/CONNECT/DEVICE/" : "/connect/device", invalidForm);
            Assert.Equal(enabled && index > 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (enabled && index > 0)
            {
                Assert.Equal(Errors.TemporarilyUnavailable, payload.RootElement.GetProperty("error").GetString());
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
            Assert.False(payload.RootElement.TryGetProperty("device_code", out _));
        }
        // GET and POST with rotating invalid codes share the verification budget.
        using var invalidGet = await factory.Client.GetAsync("/CONNECT/VERIFY/?user_code=0000-0000-0000");
        Assert.Equal(HttpStatusCode.Redirect, invalidGet.StatusCode);
        Assert.Contains("/Account/Login", invalidGet.Headers.Location?.OriginalString);
        using var invalidCode = new FormUrlEncodedContent([new("user_code", "1111-1111-1111")]);
        using var invalidPost = await factory.Client.PostAsync("/connect/verify", invalidCode);
        Assert.Equal(HttpStatusCode.Redirect, invalidPost.StatusCode);
        using var exhausted = await factory.Client.GetAsync("/connect/verify?user_code=2222-2222-2222");
        Assert.Equal(enabled ? HttpStatusCode.TooManyRequests : HttpStatusCode.Redirect, exhausted.StatusCode);
        using var discovery = await factory.Client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
    }

    private static async Task<string> RegisterClientAsync(OperationalBootstrapHostFactory factory, string consentType)
    {
        using var scope = factory.Services.CreateScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var name in new[] { Scopes.OpenId, Scopes.Profile })
            if (await scopes.FindByNameAsync(name) is null) await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = name });
        var clientId = $"device-security-{Guid.NewGuid():N}";
        await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId, ClientType = ClientTypes.Public, ConsentType = consentType,
            DisplayName = "Disposable device security client",
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
        return clientId;
    }

    private static async Task<HttpResponseMessage> PostVerificationAsync(HttpClient client, string html, string userCode, string? decision)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("user_code", userCode), new(DeviceVerificationSession.FormFieldName, HiddenValue(html, DeviceVerificationSession.FormFieldName)),
            new("__RequestVerificationToken", HiddenValue(html, "__RequestVerificationToken"))
        };
        if (decision != null) fields.Add(new("submit", decision));
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync("/connect/verify", form);
    }

    private static async Task<HttpResponseMessage> PollAsync(HttpClient client, string clientId, string deviceCode)
    {
        using var form = new FormUrlEncodedContent([
            new("grant_type", GrantTypes.DeviceCode), new("client_id", clientId), new("device_code", deviceCode)]);
        return await client.PostAsync("/connect/token", form);
    }

    private static async Task AssertPollingErrorAsync(HttpClient client, string clientId, string deviceCode, string error)
    {
        using var response = await PollAsync(client, clientId, deviceCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, payload.RootElement.GetProperty("error").GetString());
        Assert.False(payload.RootElement.TryGetProperty("access_token", out _));
    }

    private static string HiddenValue(string html, string name)
    {
        var input = Regex.Match(html, $@"<input\b[^>]*\bname=""{Regex.Escape(name)}""[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(input.Success, $"Missing {name} form field.");
        var value = Regex.Match(input.Value, @"\bvalue=""([^""]+)""", RegexOptions.IgnoreCase);
        Assert.True(value.Success, $"Missing {name} form field value.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }
}
