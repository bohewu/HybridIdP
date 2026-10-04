using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.BackgroundServices;
using Infrastructure.Options;
using Infrastructure.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OtpNet;
using Testcontainers.PostgreSql;
using Web.IdP.Extensions;
using Web.IdP.Middleware;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.SystemTests;

public sealed partial class NativePasswordRecoveryRazorSystemTests
{
    public const string SyntheticIdentifier = "synthetic-local@example.invalid";
    public const string SyntheticCode = "246810";
    private const string OriginalPassword = "Current!Password1";
    private const string SettlementTargetIdentifier = "synthetic-settlement-target@example.invalid";
    private const string SyntheticCurrentDirectoryCredential = "Synthetic!Current2";
    private const string SettlementTotpSecret = "JBSWY3DPEHPK3PXP";
    private const string PublicClientId = "testclient-public";
    private const string PublicClientRedirectUri = "https://localhost:7001/signin-oidc";

    [Fact]
    [Trait("Category", "ExplicitLocalE2E")]
    public async Task NativeRecovery_RealRazorSyntheticProof_ReachesResetForm()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_NATIVE_RECOVERY_BROWSER_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var readyFile = RequireEnvironmentPath("NATIVE_RECOVERY_BROWSER_READY_FILE");
        var stopFile = RequireEnvironmentPath("NATIVE_RECOVERY_BROWSER_STOP_FILE");
        await using var factory = await NativeRecoveryKestrelFactory.CreateAsync();
        try
        {
            using (var english = await factory.Client.GetAsync("/Account/ForgotPassword?culture=en-US"))
            {
                Assert.Equal(HttpStatusCode.OK, english.StatusCode);
                Assert.Contains("Reset your password", await english.Content.ReadAsStringAsync());
            }

            using (var traditionalChinese = await factory.Client.GetAsync(
                       "/Account/ForgotPassword?culture=zh-TW&ui-culture=zh-TW"))
            {
                Assert.Equal(HttpStatusCode.OK, traditionalChinese.StatusCode);
                Assert.Contains(
                    HtmlEncoder.Default.Encode("重設密碼"),
                    await traditionalChinese.Content.ReadAsStringAsync());
            }

            using (var missingAntiforgery = await factory.Client.PostAsync(
                       "/Account/ForgotPassword?handler=Start",
                       new FormUrlEncodedContent(new Dictionary<string, string>
                       {
                           ["Identifier.Value"] = SyntheticIdentifier
                       })))
            {
                Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
            }

            await File.WriteAllTextAsync(
                readyFile,
                new Uri(factory.Client.BaseAddress!, "/Account/ForgotPassword").AbsoluteUri);
            Assert.True(
                await WaitForFileAsync(stopFile, TimeSpan.FromMinutes(5)),
                "The bounded native-recovery browser exercise did not signal completion in time.");

            Assert.True(factory.ProofService.StartCount >= 1);
            Assert.True(factory.ProofService.VerifyCount >= 1);
            Assert.Equal(0, factory.ResetService.ResetCount);
        }
        finally
        {
            TryDelete(readyFile);
            TryDelete(stopFile);
        }
    }

    [Fact]
    [Trait("Category", "ExplicitLocalE2E")]
    public async Task PendingSettlement_RealServicesSyntheticVerifier_BrowserJourneyResolvesOnce()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_PENDING_SETTLEMENT_BROWSER_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var readyFile = RequireEnvironmentPath("PENDING_SETTLEMENT_BROWSER_READY_FILE");
        var stopFile = RequireEnvironmentPath("PENDING_SETTLEMENT_BROWSER_STOP_FILE");
        await using var factory = await NativeRecoveryKestrelFactory.CreateSettlementServicesAsync();
        try
        {
            using (var anonymousPending = await factory.Client.GetAsync(
                       $"/api/admin/users/{factory.SettlementTargetId}/credential-recovery/pending-directory-operation"))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousPending.StatusCode);
            }

            await File.WriteAllTextAsync(
                readyFile,
                JsonSerializer.Serialize(new
                {
                    baseUrl = factory.Client.BaseAddress!.AbsoluteUri.TrimEnd('/'),
                    targetIdentifier = SettlementTargetIdentifier,
                    targetRecoveryAddress = factory.SettlementRecoveryAddress
                }));
            Assert.True(
                await WaitForFileAsync(stopFile, TimeSpan.FromMinutes(7)),
                "The bounded pending-settlement browser exercise did not signal completion in time.");

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking()
                .SingleAsync(candidate => candidate.LocalAccountId == factory.SettlementTargetId);
            var preparation = await dbContext.DirectorySettlementPreparations.AsNoTracking().SingleAsync();
            var challenge = await dbContext.RecoveryProofChallenges.AsNoTracking()
                .SingleAsync(candidate => candidate.Purpose == RecoveryProofPurpose.PendingDirectorySettlement);
            var target = await dbContext.Users.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == factory.SettlementTargetId);
            var session = await dbContext.UserSessions.AsNoTracking()
                .SingleAsync(candidate => candidate.UserId == factory.SettlementTargetId);
            var authorizationManager =
                scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var authorizations = new List<object>();
            await foreach (var authorization in authorizationManager.FindBySubjectAsync(
                               factory.SettlementTargetId.ToString()))
            {
                authorizations.Add(authorization);
            }
            var tokens = new List<object>();
            await foreach (var token in tokenManager.FindBySubjectAsync(
                               factory.SettlementTargetId.ToString()))
            {
                tokens.Add(token);
            }

            Assert.Equal(NativeDirectoryRecoveryStatus.OperatorResolved, attempt.Status);
            Assert.Equal(DirectorySettlementPreparationStatus.Consumed, preparation.Status);
            Assert.NotNull(challenge.ConsumedAtUtc);
            Assert.True(target.RequiresPasswordChange);
            Assert.Equal(factory.SettlementOriginalPasswordHash, target.PasswordHash);
            Assert.NotEqual(factory.SettlementOriginalSecurityStamp, target.SecurityStamp);
            Assert.NotNull(session.RevokedUtc);
            Assert.Equal("directory-credential-operator-resolution", session.RevocationReason);
            Assert.Single(authorizations);
            foreach (var authorization in authorizations)
            {
                Assert.Equal(Statuses.Revoked, await authorizationManager.GetStatusAsync(authorization));
            }
            Assert.Equal(2, tokens.Count);
            var tokenTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in tokens)
            {
                Assert.Equal(Statuses.Revoked, await tokenManager.GetStatusAsync(token));
                tokenTypes.Add(await tokenManager.GetTypeAsync(token) ?? string.Empty);
            }
            Assert.Contains(TokenTypeIdentifiers.AccessToken, tokenTypes);
            Assert.Contains(TokenTypeIdentifiers.RefreshToken, tokenTypes);
            Assert.Equal(1, factory.SyntheticDirectoryVerifier.VerifyCalls);
            Assert.Equal(factory.SettlementDirectoryObjectId, factory.SyntheticDirectoryVerifier.VerifiedObjectId);
        }
        finally
        {
            TryDelete(readyFile);
            TryDelete(stopFile);
        }
    }

    [Fact]
    [Trait("Category", "ExplicitLocalE2E")]
    public async Task NativeRecovery_RealServicesMailpitHttpFlow_CompletesLocalReset()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_NATIVE_RECOVERY_REAL_HTTP_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var factory = await NativeRecoveryKestrelFactory.CreateRealServicesAsync(
            seedSyntheticSession: true);
        using var startPage = await factory.Client.GetAsync("/Account/ForgotPassword");
        Assert.Equal(HttpStatusCode.OK, startPage.StatusCode);
        var startHtml = await startPage.Content.ReadAsStringAsync();
        var antiforgeryToken = ExtractAntiforgeryToken(startHtml);

        using var startResponse = await factory.Client.PostAsync(
            "/Account/ForgotPassword?handler=Start",
            CreateForm(
                antiforgeryToken,
                ("Identifier.Value", SyntheticIdentifier)));
        var codeHtml = await ReadExpectedPageAsync(
            startResponse,
            "data-test-id=\"native-recovery-code\"",
            "awaiting_code");
        antiforgeryToken = ExtractAntiforgeryToken(codeHtml);

        Guid requestId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            requestId = await dbContext.RecoveryProofChallenges.AsNoTracking()
                .Where(challenge =>
                    challenge.LocalAccountId == factory.UserId &&
                    challenge.Purpose == RecoveryProofPurpose.NativePasswordRecovery)
                .OrderByDescending(challenge => challenge.CreatedAtUtc)
                .Select(challenge => challenge.Id)
                .FirstAsync();
        }

        var actualCode = await WaitForMailpitCodeAsync(factory.RecoveryAddress);
        var wrongCode = string.Equals(actualCode, "000000", StringComparison.Ordinal)
            ? "999999"
            : "000000";
        using var wrongCodeResponse = await factory.Client.PostAsync(
            "/Account/ForgotPassword?handler=Verify",
            CreateForm(
                antiforgeryToken,
                ("Verification.Code", wrongCode)));
        var wrongCodeHtml = await ReadExpectedPageAsync(
            wrongCodeResponse,
            "data-test-id=\"native-recovery-error\"",
            "verification_rejected");
        antiforgeryToken = ExtractAntiforgeryToken(wrongCodeHtml);

        using var verifyResponse = await factory.Client.PostAsync(
            "/Account/ForgotPassword?handler=Verify",
            CreateForm(
                antiforgeryToken,
                ("Verification.Code", actualCode)));
        var passwordHtml = await ReadExpectedPageAsync(
            verifyResponse,
            "data-test-id=\"native-recovery-password\"",
            "awaiting_password");
        antiforgeryToken = ExtractAntiforgeryToken(passwordHtml);

        var newPassword = $"A!a1{Guid.NewGuid():N}";
        using var resetResponse = await factory.Client.PostAsync(
            "/Account/ForgotPassword?handler=Reset",
            CreateForm(
                antiforgeryToken,
                ("Password.NewPassword", newPassword),
                ("Password.ConfirmPassword", newPassword)));
        var resetHtml = await resetResponse.Content.ReadAsStringAsync();
        if (resetResponse.StatusCode != HttpStatusCode.OK ||
            !resetHtml.Contains("data-test-id=\"native-recovery-success\"", StringComparison.Ordinal))
        {
            Assert.Fail(
                $"Native recovery expected state=reset_succeeded; status={(int)resetResponse.StatusCode}; " +
                $"observed_state={GetPageState(resetHtml)}; reset_outcome={factory.LastResetOutcome?.ToString() ?? "not_invoked"}; " +
                $"error_codes={string.Join(',', factory.LastResetErrorCodes)}.");
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var challenge = await dbContext.RecoveryProofChallenges.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == requestId);
            var user = await dbContext.Users.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == factory.UserId);
            var recoveryEmail = await dbContext.RecoveryEmails.AsNoTracking()
                .SingleAsync(candidate => candidate.LocalAccountId == factory.UserId);
            var session = await dbContext.UserSessions.AsNoTracking()
                .SingleAsync(candidate => candidate.UserId == factory.UserId);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            Assert.Equal(2, challenge.VerificationAttempts);
            Assert.NotNull(challenge.VerifiedAtUtc);
            Assert.NotNull(challenge.ConsumedAtUtc);
            Assert.Equal(challenge.VerifiedAtUtc, recoveryEmail.VerifiedAtUtc);
            Assert.NotEqual(factory.OriginalPasswordHash, user.PasswordHash);
            Assert.NotEqual(factory.OriginalSecurityStamp, user.SecurityStamp);
            Assert.False(await userManager.CheckPasswordAsync(user, OriginalPassword));
            Assert.True(await userManager.CheckPasswordAsync(user, newPassword));
            Assert.True(
                JsonSerializer.Deserialize<List<string>>(user.PasswordHistory)?
                    .Contains(factory.OriginalPasswordHash, StringComparer.Ordinal) == true,
                "The previous synthetic password hash was not retained in password history.");
            Assert.NotNull(user.LastPasswordChangeDate);
            Assert.NotNull(session.RevokedUtc);
            Assert.Equal("native-password-recovery", session.RevocationReason);
        }
    }

    [Fact]
    [Trait("Category", "ExplicitLocalE2E")]
    public async Task NativeRecovery_Continuation_RevokesPriorAuthenticationAndRejectsReplay()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_NATIVE_RECOVERY_REAL_HTTP_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var factory = await NativeRecoveryKestrelFactory.CreateRealServicesAsync();
        using var authenticatedClient = factory.CreateBrowserClient();
        await SignInWithTotpAsync(
            authenticatedClient,
            SyntheticIdentifier,
            OriginalPassword,
            factory.TotpSecret,
            "/Account/Profile");
        await MfaEnrollmentTestClient.SetCsrfTokenAsync(authenticatedClient);

        var supersededAddress = $"superseded-{Guid.NewGuid():N}@example.invalid";
        using (var beginSuperseded = await authenticatedClient.PostAsJsonAsync(
                   "/api/account/recovery-email/change",
                   new { CandidateAddress = supersededAddress }))
        {
            Assert.Equal(HttpStatusCode.OK, beginSuperseded.StatusCode);
        }
        var supersededCode = await WaitForMailpitCodeAsync(
            supersededAddress,
            "Verify recovery email");

        var finalAddress = factory.RecoveryAddress;
        using (var beginFinal = await authenticatedClient.PostAsJsonAsync(
                   "/api/account/recovery-email/change",
                   new { CandidateAddress = finalAddress }))
        {
            Assert.Equal(HttpStatusCode.OK, beginFinal.StatusCode);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pendingRecovery = await dbContext.RecoveryEmails.AsNoTracking()
                .SingleAsync(candidate => candidate.LocalAccountId == factory.UserId);
            Assert.Equal(finalAddress, pendingRecovery.Address);
            Assert.Null(pendingRecovery.VerifiedAtUtc);
        }
        var finalCode = await WaitForMailpitCodeAsync(finalAddress, "Verify recovery email");

        using (var supersededVerification = await authenticatedClient.PostAsJsonAsync(
                   "/api/account/recovery-email/verify",
                   new { Code = supersededCode }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, supersededVerification.StatusCode);
        }
        using (var finalVerification = await authenticatedClient.PostAsJsonAsync(
                   "/api/account/recovery-email/verify",
                   new { Code = finalCode }))
        {
            Assert.Equal(HttpStatusCode.OK, finalVerification.StatusCode);
        }
        using (var repeatedVerification = await authenticatedClient.PostAsJsonAsync(
                   "/api/account/recovery-email/verify",
                   new { Code = finalCode }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, repeatedVerification.StatusCode);
        }

        await AssertRecoveryAddressStateAsync(factory, supersededAddress, finalAddress);

        var tokens = await IssueAuthorizationCodeTokensAsync(authenticatedClient);
        using (var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"))
        {
            userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            using var userInfoResponse = await authenticatedClient.SendAsync(userInfoRequest);
            Assert.Equal(HttpStatusCode.OK, userInfoResponse.StatusCode);
        }

        var authorizationId = await CaptureIssuedAuthorizationAsync(factory);
        var newPassword = $"A!a1{Guid.NewGuid():N}";
        var resetSubmission = await CompleteNativeResetAsync(factory.Client, finalAddress, newPassword);

        var replayPassword = $"B!b2{Guid.NewGuid():N}";
        using (var replayResponse = await factory.Client.PostAsync(
                   "/Account/ForgotPassword?handler=Reset",
                   CreateForm(
                       resetSubmission.AntiforgeryToken,
                       ("Password.NewPassword", replayPassword),
                       ("Password.ConfirmPassword", replayPassword))))
        {
            var replayHtml = await replayResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("data-test-id=\"native-recovery-success\"", replayHtml, StringComparison.Ordinal);
        }

        await AssertRevokedStateAsync(factory, authorizationId, newPassword, replayPassword);

        using (var refreshResponse = await authenticatedClient.PostAsync(
                   "/connect/token",
                   new FormUrlEncodedContent(new Dictionary<string, string>
                   {
                       ["grant_type"] = "refresh_token",
                       ["client_id"] = PublicClientId,
                       ["refresh_token"] = tokens.RefreshToken
                   })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);
            using var payload = await JsonDocument.ParseAsync(
                await refreshResponse.Content.ReadAsStreamAsync());
            Assert.Equal("invalid_grant", payload.RootElement.GetProperty("error").GetString());
        }

        using (var postResetUserInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo"))
        {
            postResetUserInfoRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            using var postResetUserInfoResponse = await authenticatedClient.SendAsync(postResetUserInfoRequest);
            Assert.Contains(
                postResetUserInfoResponse.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
            Console.WriteLine(
                $"Sanitized access-token observation: local userinfo status={(int)postResetUserInfoResponse.StatusCode}; " +
                "configured access-token lifetime=15 minutes; external self-contained-token consumers were not exercised.");
        }

        await Task.Delay(TimeSpan.FromSeconds(65));
        using (var oldCookieResponse = await authenticatedClient.GetAsync("/Account/Profile"))
        {
            Assert.Equal(HttpStatusCode.Redirect, oldCookieResponse.StatusCode);
            Assert.Contains(
                "/Account/Login",
                oldCookieResponse.Headers.Location?.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        using var oldPasswordClient = factory.CreateBrowserClient();
        Assert.False(await PasswordLoginAdvancesAsync(
            oldPasswordClient,
            SyntheticIdentifier,
            OriginalPassword,
            "/Account/Profile"));

        using var newPasswordClient = factory.CreateBrowserClient();
        await SignInWithTotpAsync(
            newPasswordClient,
            SyntheticIdentifier,
            newPassword,
            factory.TotpSecret,
            "/Account/Profile");
        using var profileResponse = await newPasswordClient.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
    }

    private static async Task SignInWithTotpAsync(
        HttpClient client,
        string username,
        string password,
        string totpSecret,
        string returnUrl)
    {
        var loginUrl = $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        using var loginPage = await client.GetAsync(loginUrl);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginToken = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        using var loginResponse = await client.PostAsync(
            loginUrl,
            CreateForm(
                loginToken,
                ("Input.Login", username),
                ("Input.Password", password)));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains(
            "/Account/LoginTotp",
            loginResponse.Headers.Location?.ToString(),
            StringComparison.OrdinalIgnoreCase);

        using var totpPage = await client.GetAsync(loginResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, totpPage.StatusCode);
        var totpToken = ExtractAntiforgeryToken(await totpPage.Content.ReadAsStringAsync());
        var totpCode = new Totp(Base32Encoding.ToBytes(totpSecret)).ComputeTotp();

        using var totpResponse = await client.PostAsync(
            loginResponse.Headers.Location,
            CreateForm(
                totpToken,
                ("Input.TotpCode", totpCode),
                ("ReturnUrl", returnUrl),
                ("RememberMe", "false")));
        Assert.Equal(HttpStatusCode.Redirect, totpResponse.StatusCode);
        Assert.Equal(returnUrl, totpResponse.Headers.Location?.ToString());
    }

    private static async Task<bool> PasswordLoginAdvancesAsync(
        HttpClient client,
        string username,
        string password,
        string returnUrl)
    {
        var loginUrl = $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        using var loginPage = await client.GetAsync(loginUrl);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var token = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        using var response = await client.PostAsync(
            loginUrl,
            CreateForm(
                token,
                ("Input.Login", username),
                ("Input.Password", password)));
        return response.StatusCode == HttpStatusCode.Redirect &&
            response.Headers.Location?.ToString().Contains(
                "/Account/LoginTotp",
                StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task<(string AccessToken, string RefreshToken)> IssueAuthorizationCodeTokensAsync(
        HttpClient client)
    {
        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        const string scopes = "openid profile offline_access";
        var authorizeUrl =
            $"/connect/authorize?client_id={PublicClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(PublicClientRedirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            "&prompt=consent" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            "&code_challenge_method=S256";

        using var consentPage = await client.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
        var consentHtml = await consentPage.Content.ReadAsStringAsync();
        var consentFields = ExtractNamedInputValues(consentHtml);
        consentFields.Add(new KeyValuePair<string, string>("submit", "allow"));
        consentFields.Add(new KeyValuePair<string, string>("granted_scopes", scopes));

        using var consentResponse = await client.PostAsync(
            authorizeUrl,
            new FormUrlEncodedContent(consentFields));
        Assert.Equal(HttpStatusCode.Redirect, consentResponse.StatusCode);
        var callback = consentResponse.Headers.Location;
        Assert.NotNull(callback);
        var code = HttpUtility.ParseQueryString(callback!.Query)["code"];
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = PublicClientId,
                ["code"] = code!,
                ["redirect_uri"] = PublicClientRedirectUri,
                ["code_verifier"] = codeVerifier
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenPayload = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync());
        return (
            tokenPayload.RootElement.GetProperty("access_token").GetString()!,
            tokenPayload.RootElement.GetProperty("refresh_token").GetString()!);
    }

    private static async Task AssertRecoveryAddressStateAsync(
        NativeRecoveryKestrelFactory factory,
        string supersededAddress,
        string finalAddress)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == factory.UserId);
        var recovery = await dbContext.RecoveryEmails.AsNoTracking()
            .SingleAsync(candidate => candidate.LocalAccountId == factory.UserId);
        var challenges = await dbContext.RecoveryProofChallenges.AsNoTracking()
            .Where(candidate =>
                candidate.LocalAccountId == factory.UserId &&
                candidate.Purpose == RecoveryProofPurpose.RecoveryAddressVerification)
            .OrderBy(candidate => candidate.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(SyntheticIdentifier, user.Email);
        Assert.Equal(finalAddress, recovery.Address);
        Assert.NotNull(recovery.VerifiedAtUtc);
        Assert.Equal(2, challenges.Count);
        Assert.NotNull(challenges[0].RevokedAtUtc);
        Assert.NotNull(challenges[1].ConsumedAtUtc);
        Assert.Equal(0, await dbContext.ProviderMetadataSnapshots.CountAsync());
        Assert.NotEqual(supersededAddress, recovery.Address);
    }

    private static async Task<string> CaptureIssuedAuthorizationAsync(
        NativeRecoveryKestrelFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authorizationManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorizations = new List<object>();
        await foreach (var authorization in authorizationManager.FindBySubjectAsync(
                           factory.UserId.ToString()))
        {
            authorizations.Add(authorization);
        }

        var issuedAuthorization = Assert.Single(authorizations);
        Assert.Equal(Statuses.Valid, await authorizationManager.GetStatusAsync(issuedAuthorization));
        var authorizationId = await authorizationManager.GetIdAsync(issuedAuthorization);
        Assert.False(string.IsNullOrWhiteSpace(authorizationId));

        var activeTokenTypes = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var token in tokenManager.FindByAuthorizationIdAsync(authorizationId!))
        {
            if (string.Equals(await tokenManager.GetStatusAsync(token), Statuses.Valid, StringComparison.Ordinal))
            {
                activeTokenTypes.Add(await tokenManager.GetTypeAsync(token) ?? string.Empty);
            }
        }
        Assert.Contains(TokenTypeIdentifiers.AccessToken, activeTokenTypes);
        Assert.Contains(TokenTypeIdentifiers.RefreshToken, activeTokenTypes);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await dbContext.UserSessions.SingleAsync(
            candidate => candidate.UserId == factory.UserId);
        Assert.Equal(authorizationId, session.AuthorizationId);
        Assert.Equal(PublicClientId, session.ClientId);
        Assert.Equal(factory.ActiveRoleId, session.ActiveRoleId);
        Assert.Null(session.CurrentRefreshTokenHash);
        Assert.Null(session.PreviousRefreshTokenHash);
        Assert.Null(session.RevokedUtc);
        return authorizationId!;
    }

    private static async Task AssertRevokedStateAsync(
        NativeRecoveryKestrelFactory factory,
        string authorizationId,
        string newPassword,
        string replayPassword)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == factory.UserId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.False(await userManager.CheckPasswordAsync(user, OriginalPassword));
        Assert.True(await userManager.CheckPasswordAsync(user, newPassword));
        Assert.False(await userManager.CheckPasswordAsync(user, replayPassword));

        var session = await dbContext.UserSessions.AsNoTracking()
            .SingleAsync(candidate => candidate.UserId == factory.UserId);
        Assert.NotNull(session.RevokedUtc);
        Assert.Equal("native-password-recovery", session.RevocationReason);

        var authorizationManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizationManager.FindByIdAsync(authorizationId);
        Assert.NotNull(authorization);
        Assert.Equal(Statuses.Revoked, await authorizationManager.GetStatusAsync(authorization!));
    }

    private static async Task<(string AntiforgeryToken, string Html)> CompleteNativeResetAsync(
        HttpClient client,
        string recoveryAddress,
        string newPassword)
    {
        using var startPage = await client.GetAsync("/Account/ForgotPassword");
        Assert.Equal(HttpStatusCode.OK, startPage.StatusCode);
        var antiforgeryToken = ExtractAntiforgeryToken(await startPage.Content.ReadAsStringAsync());
        using var startResponse = await client.PostAsync(
            "/Account/ForgotPassword?handler=Start",
            CreateForm(antiforgeryToken, ("Identifier.Value", SyntheticIdentifier)));
        var codeHtml = await ReadExpectedPageAsync(
            startResponse,
            "data-test-id=\"native-recovery-code\"",
            "awaiting_code");
        antiforgeryToken = ExtractAntiforgeryToken(codeHtml);
        var code = await WaitForMailpitCodeAsync(
            recoveryAddress,
            "Password recovery verification");
        using var verifyResponse = await client.PostAsync(
            "/Account/ForgotPassword?handler=Verify",
            CreateForm(antiforgeryToken, ("Verification.Code", code)));
        var passwordHtml = await ReadExpectedPageAsync(
            verifyResponse,
            "data-test-id=\"native-recovery-password\"",
            "awaiting_password");
        antiforgeryToken = ExtractAntiforgeryToken(passwordHtml);
        using var resetResponse = await client.PostAsync(
            "/Account/ForgotPassword?handler=Reset",
            CreateForm(
                antiforgeryToken,
                ("Password.NewPassword", newPassword),
                ("Password.ConfirmPassword", newPassword)));
        var resetHtml = await ReadExpectedPageAsync(
            resetResponse,
            "data-test-id=\"native-recovery-success\"",
            "reset_succeeded");
        return (antiforgeryToken, resetHtml);
    }

    private static List<KeyValuePair<string, string>> ExtractNamedInputValues(string html)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var name = ExtractAttribute(input.Value, "name");
            var value = ExtractAttribute(input.Value, "value");
            if (!string.IsNullOrEmpty(name) && value is not null)
            {
                fields.Add(new KeyValuePair<string, string>(name, WebUtility.HtmlDecode(value)));
            }
        }
        return fields;
    }

    private static string? ExtractAttribute(string tag, string attribute)
    {
        var match = Regex.Match(
            tag,
            $"\\b{Regex.Escape(attribute)}=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static FormUrlEncodedContent CreateForm(
        string antiforgeryToken,
        params (string Name, string Value)[] values) =>
        new(values
            .Append((Name: "__RequestVerificationToken", Value: antiforgeryToken))
            .Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        }

        Assert.True(match.Success, "Expected antiforgery field was absent from the Razor response.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<string> ReadExpectedPageAsync(
        HttpResponseMessage response,
        string expectedMarker,
        string expectedState)
    {
        var html = await response.Content.ReadAsStringAsync();
        var observedState = GetPageState(html);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK &&
            html.Contains(expectedMarker, StringComparison.Ordinal),
            $"Native recovery expected state={expectedState}; status={(int)response.StatusCode}; observed_state={observedState}.");
        return html;
    }

    private static string GetPageState(string html)
    {
        if (html.Contains("data-test-id=\"native-recovery-success\"", StringComparison.Ordinal))
        {
            return "reset_succeeded";
        }

        if (html.Contains("data-test-id=\"native-recovery-password\"", StringComparison.Ordinal))
        {
            return "awaiting_password";
        }

        if (html.Contains("data-test-id=\"native-recovery-code\"", StringComparison.Ordinal))
        {
            return html.Contains("data-test-id=\"native-recovery-error\"", StringComparison.Ordinal)
                ? "verification_rejected"
                : "awaiting_code";
        }

        return html.Contains("data-test-id=\"native-recovery-error\"", StringComparison.Ordinal)
            ? "request_rejected"
            : "unknown";
    }

    private static async Task<string> WaitForMailpitCodeAsync(
        string expectedAddress,
        string expectedSubject = "Password recovery verification")
    {
        var configuredPort = Environment.GetEnvironmentVariable("MAILPIT_HTTP_PORT");
        var port = int.TryParse(configuredPort, out var parsedPort) && parsedPort is > 0 and <= 65535
            ? parsedPort
            : 8025;
        using var mailpitClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}")
        };
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await mailpitClient.GetAsync("/api/v1/messages");
            if (response.IsSuccessStatusCode)
            {
                using var messages = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (messages.RootElement.TryGetProperty("messages", out var items))
                {
                    foreach (var message in items.EnumerateArray())
                    {
                        if (!HasRecipient(message, expectedAddress) ||
                            !message.TryGetProperty("Subject", out var subject) ||
                            !string.Equals(
                                subject.GetString(),
                                expectedSubject,
                                StringComparison.Ordinal) ||
                            !message.TryGetProperty("Snippet", out var snippet))
                        {
                            continue;
                        }

                        var code = Regex.Match(snippet.GetString() ?? string.Empty, @"\b[0-9]{6}\b");
                        if (code.Success)
                        {
                            return code.Value;
                        }
                    }
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail("Mailpit did not expose a recovery message for the isolated synthetic recipient.");
        return string.Empty;
    }

    private static bool HasRecipient(JsonElement message, string expectedAddress) =>
        message.TryGetProperty("To", out var recipients) &&
        recipients.ValueKind == JsonValueKind.Array &&
        recipients.EnumerateArray().Any(recipient =>
            recipient.TryGetProperty("Address", out var address) &&
            string.Equals(address.GetString(), expectedAddress, StringComparison.OrdinalIgnoreCase));

    private static string RequireEnvironmentPath(string name) =>
        Path.GetFullPath(Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required for the explicit local E2E."));

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private sealed class NativeRecoveryKestrelFactory : WebApplicationFactory<SecurityHeadersMiddleware>
    {
        private const string TrustedDevelopmentCertificateThumbprint =
            "8DA113DE2658966AF2CC40FD57F26BA90A761AAB";
        private static readonly TimeSpan GuidanceStartupLockTimeout = TimeSpan.FromSeconds(15);
        private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
        private readonly string _databaseName = $"native-recovery-browser-{Guid.NewGuid():N}";
        private readonly bool _realServices;
        private readonly int _mailpitSmtpPort;
        private readonly bool _seedSyntheticSession;
        private readonly bool _settlementServices;
        private readonly bool _connectedDirectoryServices;
        private readonly bool _guidanceTestServer;
        private readonly Guid? _connectedSettlementDirectoryObjectId;
        private readonly string? _connectedSettlementTargetIdentifier;
        private PostgreSqlContainer? _postgresContainer;
        private X509Certificate2? _serverCertificate;
        private Action<ForgotPasswordRecoveryOptions>? _configureGuidance;

        public HttpClient Client { get; private set; } = null!;
        public FakeNativeProofService ProofService { get; } = new();
        public FakeNativeResetService ResetService { get; } = new();
        public Guid UserId { get; private set; }
        public Guid ActiveRoleId { get; private set; }
        public string RecoveryAddress { get; } = $"native-recovery-{Guid.NewGuid():N}@example.invalid";
        public string TotpSecret { get; private set; } = string.Empty;
        public string OriginalPasswordHash { get; private set; } = string.Empty;
        public string OriginalSecurityStamp { get; private set; } = string.Empty;
        public NativeRecoveryResetOutcome? LastResetOutcome { get; private set; }
        public IReadOnlyList<string> LastResetErrorCodes { get; private set; } = [];
        public Guid SettlementTargetId { get; private set; }
        public Guid SettlementDirectoryObjectId { get; private set; }
        public string SettlementRecoveryAddress { get; } =
            $"synthetic-settlement-{Guid.NewGuid():N}@example.invalid";
        public string SettlementOriginalPasswordHash { get; private set; } = string.Empty;
        public string SettlementOriginalSecurityStamp { get; private set; } = string.Empty;
        public SyntheticSettlementDirectoryVerifier SyntheticDirectoryVerifier { get; } = new();
        public ConnectedDirectoryVerifierProbe ConnectedDirectoryVerifier { get; } = new();
        public string SettlementTargetIdentifier { get; private set; } =
            NativePasswordRecoveryRazorSystemTests.SettlementTargetIdentifier;

        private NativeRecoveryKestrelFactory(
            bool realServices,
            int mailpitSmtpPort,
            bool seedSyntheticSession = false,
            bool settlementServices = false,
            bool connectedDirectoryServices = false,
            Guid? connectedSettlementDirectoryObjectId = null,
            string? connectedSettlementTargetIdentifier = null,
            bool guidanceTestServer = false)
        {
            _realServices = realServices;
            _mailpitSmtpPort = mailpitSmtpPort;
            _seedSyntheticSession = seedSyntheticSession;
            _settlementServices = settlementServices;
            _connectedDirectoryServices = connectedDirectoryServices;
            _guidanceTestServer = guidanceTestServer;
            _connectedSettlementDirectoryObjectId = connectedSettlementDirectoryObjectId;
            _connectedSettlementTargetIdentifier = connectedSettlementTargetIdentifier;
            ConnectedDirectoryVerifier.ExpectedObjectId = connectedSettlementDirectoryObjectId ?? Guid.Empty;
            ConnectedDirectoryVerifier.ExpectedCanonicalAccount = connectedSettlementTargetIdentifier ?? string.Empty;
        }

        public static Task<NativeRecoveryKestrelFactory> CreateAsync() =>
            CreateAsync(realServices: false, mailpitSmtpPort: 0);

        public static Task<NativeRecoveryKestrelFactory> CreateGuidanceAsync(
            Action<ForgotPasswordRecoveryOptions> configureGuidance) =>
            CreateAsync(realServices: false, mailpitSmtpPort: 0, configureGuidance: configureGuidance);

        public static Task<NativeRecoveryKestrelFactory> CreateGuidanceTestServerAsync(
            Action<ForgotPasswordRecoveryOptions> configureGuidance) =>
            CreateAsync(
                realServices: false,
                mailpitSmtpPort: 0,
                configureGuidance: configureGuidance,
                guidanceTestServer: true);

        public static Task<NativeRecoveryKestrelFactory> CreateRealServicesAsync(
            bool seedSyntheticSession = false)
        {
            var configuredPort = Environment.GetEnvironmentVariable("MAILPIT_SMTP_PORT");
            return int.TryParse(configuredPort, out var port) && port is > 0 and <= 65535
                ? CreateAsync(realServices: true, mailpitSmtpPort: port, seedSyntheticSession)
                : throw new InvalidOperationException(
                    "MAILPIT_SMTP_PORT must be a valid port for the explicit local E2E.");
        }

        public static Task<NativeRecoveryKestrelFactory> CreateSettlementServicesAsync()
        {
            var configuredPort = Environment.GetEnvironmentVariable("MAILPIT_SMTP_PORT");
            return int.TryParse(configuredPort, out var port) && port is > 0 and <= 65535
                ? CreateAsync(realServices: true, mailpitSmtpPort: port, settlementServices: true)
                : throw new InvalidOperationException(
                    "MAILPIT_SMTP_PORT must be a valid port for the explicit local E2E.");
        }

        public static Task<NativeRecoveryKestrelFactory> CreateConnectedDirectoryServicesAsync()
        {
            var configuredPort = Environment.GetEnvironmentVariable("MAILPIT_SMTP_PORT");
            return int.TryParse(configuredPort, out var port) && port is > 0 and <= 65535
                ? CreateAsync(realServices: true, mailpitSmtpPort: port, connectedDirectoryServices: true)
                : throw new InvalidOperationException(
                    "MAILPIT_SMTP_PORT must be a valid port for the explicit connected E2E.");
        }

        public static Task<NativeRecoveryKestrelFactory> CreateConnectedSettlementServicesAsync(
            Guid directoryObjectId,
            string targetIdentifier)
        {
            var configuredPort = Environment.GetEnvironmentVariable("MAILPIT_SMTP_PORT");
            return int.TryParse(configuredPort, out var port) && port is > 0 and <= 65535
                ? CreateAsync(
                    realServices: true,
                    mailpitSmtpPort: port,
                    settlementServices: true,
                    connectedDirectoryServices: true,
                    connectedSettlementDirectoryObjectId: directoryObjectId,
                    connectedSettlementTargetIdentifier: targetIdentifier)
                : throw new InvalidOperationException(
                    "MAILPIT_SMTP_PORT must be a valid port for the explicit connected E2E.");
        }

        private static async Task<NativeRecoveryKestrelFactory> CreateAsync(
            bool realServices,
            int mailpitSmtpPort,
            bool seedSyntheticSession = false,
            bool settlementServices = false,
            bool connectedDirectoryServices = false,
            Guid? connectedSettlementDirectoryObjectId = null,
            string? connectedSettlementTargetIdentifier = null,
            Action<ForgotPasswordRecoveryOptions>? configureGuidance = null,
            bool guidanceTestServer = false)
        {
            var factory = new NativeRecoveryKestrelFactory(
                realServices,
                mailpitSmtpPort,
                seedSyntheticSession,
                settlementServices,
                connectedDirectoryServices,
                connectedSettlementDirectoryObjectId,
                connectedSettlementTargetIdentifier,
                guidanceTestServer);
            factory._configureGuidance = configureGuidance;
            try
            {
                factory.RecordGuidanceLifecycle("startup-begin");
                if (realServices)
                {
                    factory._postgresContainer = new PostgreSqlBuilder("postgres:17-alpine")
                        .WithName($"native-recovery-e2e-{Guid.NewGuid():N}")
                        .WithDatabase("native_recovery_e2e")
                        .Build();
                    await factory._postgresContainer.StartAsync();
                }

                if (guidanceTestServer)
                {
                    if (!await EnvironmentLock.WaitAsync(GuidanceStartupLockTimeout))
                    {
                        throw new TimeoutException("Guidance TestServer startup lock timed out.");
                    }
                }
                else
                {
                    await EnvironmentLock.WaitAsync();
                }
                factory.RecordGuidanceLifecycle("environment-lock-acquired");
                var providerVariable = "DATABASE_PROVIDER";
                var connectionVariable = realServices
                    ? "ConnectionStrings__PostgreSqlConnection"
                    : "ConnectionStrings__SqlServerConnection";
                var previousProvider = Environment.GetEnvironmentVariable(providerVariable);
                var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);
                try
                {
                    Environment.SetEnvironmentVariable(
                        providerVariable,
                        realServices ? "PostgreSQL" : "SqlServer");
                    Environment.SetEnvironmentVariable(
                        connectionVariable,
                        realServices
                            ? factory._postgresContainer!.GetConnectionString()
                            : "Server=(local);Database=unused");
                    if (guidanceTestServer)
                    {
                        factory.Client = factory.CreateGuidanceTestServerClient();
                    }
                    else
                    {
                        factory._serverCertificate = LoadTrustedDevelopmentCertificate();
                        factory.UseKestrel(options => options.Listen(
                            IPAddress.Loopback,
                            0,
                            listenOptions => listenOptions.UseHttps(factory._serverCertificate)));
                        factory.Client = factory.CreateDefaultClient();
                        var listenerAddress = factory.Client.BaseAddress
                            ?? throw new InvalidOperationException("Kestrel did not publish a listener address.");
                        factory.Client.BaseAddress = new UriBuilder(listenerAddress)
                        {
                            Host = "localhost"
                        }.Uri;
                    }
                    factory.RecordGuidanceLifecycle("client-created");
                }
                finally
                {
                    Environment.SetEnvironmentVariable(providerVariable, previousProvider);
                    Environment.SetEnvironmentVariable(connectionVariable, previousConnection);
                    EnvironmentLock.Release();
                    factory.RecordGuidanceLifecycle("environment-restored");
                }

                await using var scope = factory.Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
                factory.RecordGuidanceLifecycle("database-ready");
                if (realServices)
                {
                    var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
                    await ScopeSeeder.SeedAsync(scopeManager, dbContext);
                    await ClientSeeder.SeedAsync(
                        scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(),
                        scopeManager,
                        seedTestClients: true);
                    if (settlementServices)
                    {
                        await factory.SeedSettlementScenarioAsync(
                            dbContext,
                            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(),
                            scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>(),
                            scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>());
                    }
                    else
                    {
                        await factory.SeedRealServicesScenarioAsync(
                            dbContext,
                            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
                    }
                }

                factory.RecordGuidanceLifecycle("ready");
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["DatabaseProvider"] = _realServices ? "PostgreSQL" : "SqlServer",
                    ["Redis:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "true",
                    ["RateLimiting:LoginPermitLimit"] = "30",
                    ["RateLimiting:LoginWindowSeconds"] = "60",
                    ["RateLimiting:QueueLimit"] = "0",
                    ["Turnstile:Enabled"] = "false",
                    ["SeedData:PrivilegedTestAdminBootstrap:Enabled"] = "false",
                    ["ForgotPasswordRecovery:DeploymentCeiling"] = "Native",
                    ["ForgotPasswordRecovery:NativeRecoveryEnabled"] = "true",
                    ["ForgotPasswordRecovery:NativeDirectoryRecoveryEnabled"] =
                        _connectedDirectoryServices.ToString(),
                    ["ForgotPasswordRecovery:OrdinaryRecoveryAssistanceEnabled"] =
                        _connectedDirectoryServices.ToString(),
                    ["ForgotPasswordRecovery:PendingDirectorySettlementEnabled"] =
                        _settlementServices.ToString(),
                    ["DirectoryIntegration:Enabled"] = (_settlementServices || _connectedDirectoryServices).ToString(),
                    ["DirectoryIntegration:AuthenticationEnabled"] = _connectedDirectoryServices.ToString(),
                    ["DirectoryIntegration:TemporaryCredentialCapabilityEnabled"] = _connectedDirectoryServices.ToString(),
                    ["DirectoryIntegration:Transport"] = _connectedDirectoryServices
                        ? "WindowsNegotiate"
                        : "Ldaps",
                    ["CredentialMigration:RecoveryEmailEnabled"] = _realServices.ToString()
                };
                if (_guidanceTestServer)
                {
                    values["OpenIddict:UseEphemeralKeysForTesting"] = "true";
                }
                if (_settlementServices)
                {
                    values["ProviderProof:Endpoint"] = "https://127.0.0.1:1/synthetic-unused";
                    values["ProviderProof:SharedSecret"] = "synthetic-settlement-unused";
                }
                if (_connectedDirectoryServices)
                {
                    values["ProviderProof:Endpoint"] = "https://127.0.0.1:1/connected-unused";
                    values["ProviderProof:SharedSecret"] = "connected-unused";
                }
                if (_realServices)
                {
                    values["ConnectionStrings:PostgreSqlConnection"] =
                        _postgresContainer!.GetConnectionString();
                    values["RecoveryVerificationPolicy:Enabled"] = "true";
                    values["RecoveryVerificationPolicy:CurrentPeriodId"] = "native-recovery-e2e";
                    values["RecoveryVerificationPolicy:EffectiveAtUtc"] = "2026-01-01T00:00:00Z";
                    values["RecoveryVerificationPolicy:GraceEndsAtUtc"] = "2026-01-01T00:00:00Z";
                    values["EmailSettings:SmtpHost"] = "127.0.0.1";
                    values["EmailSettings:SmtpPort"] = _mailpitSmtpPort.ToString();
                    values["EmailSettings:SmtpEnableSsl"] = "false";
                    values["EmailSettings:SmtpUsername"] = string.Empty;
                    values["EmailSettings:SmtpPassword"] = string.Empty;
                    values["EmailSettings:FromAddress"] = "native-recovery-e2e@example.invalid";
                    values["EmailSettings:FromName"] = "Native Recovery E2E";
                }
                else
                {
                    values["ConnectionStrings:SqlServerConnection"] = "Server=(local);Database=unused";
                }

                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureServices((context, services) =>
            {
                if (_guidanceTestServer)
                {
                    services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                    services.AddCustomRateLimiting(context.Configuration);
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                }
                if (_configureGuidance is not null)
                {
                    services.Configure(_configureGuidance);
                }
                services.RemoveAll<IHostedService>();
                if (_realServices)
                {
                    services.Configure<EmailOptions>(options =>
                    {
                        options.SmtpHost = "127.0.0.1";
                        options.SmtpPort = _mailpitSmtpPort;
                        options.SmtpEnableSsl = false;
                        options.SmtpUsername = string.Empty;
                        options.SmtpPassword = string.Empty;
                        options.FromAddress = "native-recovery-e2e@example.invalid";
                        options.FromName = "Native Recovery E2E";
                    });
                    services.AddHostedService<EmailQueueProcessor>();
                    services.RemoveAll<INativePasswordRecoveryResetService>();
                    services.AddScoped<Infrastructure.Services.NativePasswordRecoveryResetService>();
                    services.AddScoped<INativePasswordRecoveryResetService>(provider =>
                        new CapturingNativeResetService(
                            provider.GetRequiredService<Infrastructure.Services.NativePasswordRecoveryResetService>(),
                            CaptureResetResult));
                    if (_settlementServices && !_connectedDirectoryServices)
                    {
                        services.RemoveAll<IDirectoryCredentialVerifier>();
                        services.AddSingleton<IDirectoryCredentialVerifier>(SyntheticDirectoryVerifier);
                    }
                    else if (_settlementServices && _connectedDirectoryServices)
                    {
                        services.RemoveAll<IDirectoryCredentialVerifier>();
                        services.AddScoped<IDirectoryCredentialVerifier>(provider =>
                            new ConnectedCountingDirectoryCredentialVerifier(
                                provider.GetRequiredService<Infrastructure.Directory.ProtectedDirectoryCredentialService>(),
                                ConnectedDirectoryVerifier));
                    }
                    return;
                }

                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

                services.RemoveAll<ISecurityPolicyService>();
                services.AddSingleton<ISecurityPolicyService>(new NativeSecurityPolicyService());
                services.RemoveAll<INativePasswordRecoveryProofService>();
                services.AddSingleton<INativePasswordRecoveryProofService>(ProofService);
                services.RemoveAll<INativePasswordRecoveryResetService>();
                services.AddSingleton<INativePasswordRecoveryResetService>(ResetService);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_guidanceTestServer)
            {
                Client?.Dispose();
                await base.DisposeAsync();
                _serverCertificate?.Dispose();
                _serverCertificate = null;
                if (_postgresContainer is not null)
                {
                    await _postgresContainer.DisposeAsync();
                    _postgresContainer = null;
                }
                return;
            }

            RecordGuidanceLifecycle("dispose-begin");
            Client?.Dispose();
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                RecordGuidanceLifecycle("disposed");
            }
        }

        public HttpClient CreateBrowserClient()
        {
            if (_guidanceTestServer)
            {
                return CreateGuidanceTestServerClient();
            }

            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = new CookieContainer(),
                UseCookies = true
            })
            {
                BaseAddress = Client.BaseAddress
                    ?? throw new InvalidOperationException("Kestrel did not publish a listener address.")
            };
            return client;
        }

        private HttpClient CreateGuidanceTestServerClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            return client;
        }

        private void RecordGuidanceLifecycle(string phase)
        {
            if (_guidanceTestServer)
            {
                Console.WriteLine($"[native-recovery-guidance] phase={phase}");
            }
        }

        private async Task SeedRealServicesScenarioAsync(
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager)
        {
            var now = DateTimeOffset.UtcNow;
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = SyntheticIdentifier,
                NormalizedUserName = SyntheticIdentifier.ToUpperInvariant(),
                Email = SyntheticIdentifier,
                NormalizedEmail = SyntheticIdentifier.ToUpperInvariant(),
                EmailConfirmed = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                LastPasswordChangeDate = now.AddDays(-30).UtcDateTime
            };
            var passwordHasher = new PasswordHasher<ApplicationUser>();
            user.PasswordHash = passwordHasher.HashPassword(user, OriginalPassword);
            var recoveryEmail = new RecoveryEmailRecord(
                user.Id,
                RecoveryAddress,
                RecoveryAddress.ToUpperInvariant(),
                now);
            recoveryEmail.MarkVerified(now);
            var policy = new SecurityPolicy
            {
                Id = Guid.NewGuid(),
                ForgotPasswordMode = ForgotPasswordMode.Native,
                MinPasswordLength = 12,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 5,
                PasswordExpirationDays = 180,
                MinPasswordAgeDays = 0,
                MaxFailedAccessAttempts = 5,
                LockoutDurationMinutes = 15,
                UpdatedUtc = now.UtcDateTime,
                UpdatedBy = "native-recovery-e2e"
            };
            var role = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = "NativeRecoveryE2E",
                NormalizedName = "NATIVERECOVERYE2E"
            };
            dbContext.Users.Add(user);
            dbContext.RecoveryEmails.Add(recoveryEmail);
            dbContext.SecurityPolicies.Add(policy);
            dbContext.Roles.Add(role);
            if (_seedSyntheticSession)
            {
                dbContext.UserSessions.Add(new UserSession
                {
                    UserId = user.Id,
                    AuthorizationId = Guid.NewGuid().ToString("N"),
                    ActiveRoleId = role.Id
                });
            }
            await dbContext.SaveChangesAsync();

            var roleResult = await userManager.AddToRoleAsync(user, role.Name!);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("The isolated user role could not be assigned.");
            }

            var authenticatorResult = await userManager.ResetAuthenticatorKeyAsync(user);
            if (!authenticatorResult.Succeeded)
            {
                throw new InvalidOperationException("The isolated TOTP authenticator could not be configured.");
            }
            TotpSecret = await userManager.GetAuthenticatorKeyAsync(user)
                ?? throw new InvalidOperationException("The isolated TOTP authenticator key was unavailable.");
            if (string.IsNullOrWhiteSpace(TotpSecret))
            {
                throw new InvalidOperationException("The isolated TOTP authenticator key was empty.");
            }
            var twoFactorResult = await userManager.SetTwoFactorEnabledAsync(user, true);
            if (!twoFactorResult.Succeeded)
            {
                throw new InvalidOperationException("The isolated TOTP factor could not be enabled.");
            }

            UserId = user.Id;
            ActiveRoleId = role.Id;
            OriginalPasswordHash = user.PasswordHash;
            OriginalSecurityStamp = user.SecurityStamp;
        }

        private async Task SeedSettlementScenarioAsync(
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictTokenManager tokenManager)
        {
            var now = DateTimeOffset.UtcNow;
            var passwordHasher = new PasswordHasher<ApplicationUser>();
            var operatorAccount = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = SyntheticIdentifier,
                NormalizedUserName = SyntheticIdentifier.ToUpperInvariant(),
                Email = SyntheticIdentifier,
                NormalizedEmail = SyntheticIdentifier.ToUpperInvariant(),
                EmailConfirmed = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                LastPasswordChangeDate = now.AddDays(-30).UtcDateTime
            };
            operatorAccount.PasswordHash = passwordHasher.HashPassword(operatorAccount, OriginalPassword);
            var targetIdentifier = _connectedSettlementTargetIdentifier ?? SettlementTargetIdentifier;
            var target = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = targetIdentifier,
                NormalizedUserName = targetIdentifier.ToUpperInvariant(),
                Email = NativePasswordRecoveryRazorSystemTests.SettlementTargetIdentifier,
                NormalizedEmail = NativePasswordRecoveryRazorSystemTests.SettlementTargetIdentifier.ToUpperInvariant(),
                EmailConfirmed = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            target.PasswordHash = passwordHasher.HashPassword(target, "Target!Local3");
            var operatorRole = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = "SyntheticSettlementOperator",
                NormalizedName = "SYNTHETICSETTLEMENTOPERATOR",
                Permissions = string.Join(
                    ',',
                    Core.Domain.Constants.Permissions.Users.Read,
                    Core.Domain.Constants.Permissions.Users.Update)
            };
            var recoveryEmail = new RecoveryEmailRecord(
                target.Id,
                SettlementRecoveryAddress,
                SettlementRecoveryAddress.ToUpperInvariant(),
                now);
            recoveryEmail.MarkVerified(now);
            var securityPolicy = new SecurityPolicy
            {
                Id = Guid.NewGuid(),
                ForgotPasswordMode = ForgotPasswordMode.Native,
                MinPasswordLength = 12,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 5,
                PasswordExpirationDays = 180,
                MinPasswordAgeDays = 0,
                MaxFailedAccessAttempts = 5,
                LockoutDurationMinutes = 15,
                UpdatedUtc = now.UtcDateTime,
                UpdatedBy = "synthetic-settlement-e2e"
            };
            var directoryObjectId = _connectedSettlementDirectoryObjectId ?? Guid.NewGuid();
            var binding = new ProviderSubjectDirectoryBinding(
                target.Id,
                "synthetic-settlement",
                "synthetic-subject",
                directoryObjectId,
                now.UtcDateTime,
                targetIdentifier);
            var migration = new CredentialMigrationStateRecord(target.Id, binding.Id, now);
            migration.Advance(
                CredentialMigrationState.ProofValidated,
                EffectiveEmailOtpRequirement.NotRequired,
                now);
            migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, now);
            migration.Advance(CredentialMigrationState.LocalFinalized, now);
            var attempt = new NativeDirectoryRecoveryAttempt(
                target.Id,
                directoryObjectId,
                NativeDirectoryCredentialOperationKind.RequiredChange,
                now);

            dbContext.Users.AddRange(operatorAccount, target);
            dbContext.Roles.Add(operatorRole);
            dbContext.RecoveryEmails.Add(recoveryEmail);
            dbContext.SecurityPolicies.Add(securityPolicy);
            dbContext.ProviderSubjectDirectoryBindings.Add(binding);
            dbContext.CredentialMigrationStateRecords.Add(migration);
            dbContext.NativeDirectoryRecoveryAttempts.Add(attempt);
            await dbContext.SaveChangesAsync();

            var application = await applicationManager.FindByClientIdAsync(PublicClientId)
                ?? throw new InvalidOperationException("The synthetic settlement client was unavailable.");
            var applicationId = await applicationManager.GetIdAsync(application)
                ?? throw new InvalidOperationException("The synthetic settlement client identifier was unavailable.");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(Claims.Subject, target.Id.ToString())],
                "SyntheticSettlement"));
            var authorization = await authorizationManager.CreateAsync(new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = applicationId,
                Principal = principal,
                Status = Statuses.Valid,
                Subject = target.Id.ToString(),
                Type = AuthorizationTypes.Permanent
            });
            var authorizationId = await authorizationManager.GetIdAsync(authorization)
                ?? throw new InvalidOperationException("The synthetic settlement authorization identifier was unavailable.");
            foreach (var tokenType in new[] { TokenTypeIdentifiers.AccessToken, TokenTypeIdentifiers.RefreshToken })
            {
                await tokenManager.CreateAsync(new OpenIddictTokenDescriptor
                {
                    ApplicationId = applicationId,
                    AuthorizationId = authorizationId,
                    Principal = principal,
                    Status = Statuses.Valid,
                    Subject = target.Id.ToString(),
                    Type = tokenType
                });
            }
            dbContext.UserSessions.Add(new UserSession
            {
                UserId = target.Id,
                AuthorizationId = authorizationId,
                ClientId = PublicClientId
            });
            await dbContext.SaveChangesAsync();

            var roleResult = await userManager.AddToRoleAsync(operatorAccount, operatorRole.Name!);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("The synthetic settlement operator role could not be assigned.");
            }
            var authenticatorResult = await userManager.SetAuthenticationTokenAsync(
                operatorAccount,
                "[AspNetUserStore]",
                "AuthenticatorKey",
                SettlementTotpSecret);
            if (!authenticatorResult.Succeeded)
            {
                throw new InvalidOperationException("The synthetic settlement TOTP authenticator could not be configured.");
            }
            TotpSecret = await userManager.GetAuthenticatorKeyAsync(operatorAccount)
                ?? throw new InvalidOperationException("The synthetic settlement TOTP key was unavailable.");
            if (!string.Equals(TotpSecret, SettlementTotpSecret, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The synthetic settlement TOTP key changed unexpectedly.");
            }
            var twoFactorResult = await userManager.SetTwoFactorEnabledAsync(operatorAccount, true);
            if (!twoFactorResult.Succeeded)
            {
                throw new InvalidOperationException("The synthetic settlement TOTP factor could not be enabled.");
            }

            UserId = operatorAccount.Id;
            SettlementTargetId = target.Id;
            SettlementDirectoryObjectId = directoryObjectId;
            SettlementTargetIdentifier = targetIdentifier;
            SettlementOriginalPasswordHash = target.PasswordHash;
            SettlementOriginalSecurityStamp = target.SecurityStamp;
            SyntheticDirectoryVerifier.ExpectedObjectId = directoryObjectId;
        }

        private void CaptureResetResult(NativeRecoveryResetResult result)
        {
            LastResetOutcome = result.Outcome;
            LastResetErrorCodes = result.ErrorCodes ?? [];
        }

        private static X509Certificate2 LoadTrustedDevelopmentCertificate()
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                TrustedDevelopmentCertificateThumbprint,
                validOnly: true);
            return matches.Count == 1
                ? matches[0]
                : throw new InvalidOperationException(
                    "The selected trusted ASP.NET development certificate is unavailable or invalid.");
        }
    }

    private sealed class CapturingNativeResetService(
        Infrastructure.Services.NativePasswordRecoveryResetService inner,
        Action<NativeRecoveryResetResult> capture) : INativePasswordRecoveryResetService
    {
        public async Task<NativeRecoveryResetResult> ResetAsync(
            NativeRecoveryResetRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.ResetAsync(request, cancellationToken);
            capture(result);
            return result;
        }
    }

    private sealed class NativeSecurityPolicyService : ISecurityPolicyService
    {
        private static readonly SecurityPolicy Policy = new()
        {
            ForgotPasswordMode = ForgotPasswordMode.Native
        };

        public Task<SecurityPolicy> GetCurrentPolicyAsync() => Task.FromResult(Policy);

        public Task<SecurityPolicy> GetCurrentPolicyForPasskeyAuthenticationAsync(
            CancellationToken ct = default) =>
            Task.FromResult(Policy);

        public Task UpdatePolicyAsync(SecurityPolicyDto policyDto, string updatedBy) =>
            throw new NotSupportedException();
    }

    private sealed class FakeNativeProofService : INativePasswordRecoveryProofService
    {
        public int StartCount { get; private set; }
        public int VerifyCount { get; private set; }

        public Task<NativeRecoveryStartResult> StartAsync(
            NativeRecoveryStartRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult(new NativeRecoveryStartResult(Guid.NewGuid()));
        }

        public Task<NativeRecoveryVerificationResult> VerifyAsync(
            NativeRecoveryVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            VerifyCount++;
            return Task.FromResult(
                string.Equals(request.Code, SyntheticCode, StringComparison.Ordinal)
                    ? new NativeRecoveryVerificationResult(
                        NativeRecoveryVerificationOutcome.Verified,
                        "synthetic-browser-proof")
                    : new NativeRecoveryVerificationResult(
                        NativeRecoveryVerificationOutcome.Denied));
        }
    }

    private sealed class FakeNativeResetService : INativePasswordRecoveryResetService
    {
        public int ResetCount { get; private set; }
        public NativeRecoveryResetOutcome Outcome { get; set; } = NativeRecoveryResetOutcome.Denied;

        public Task<NativeRecoveryResetResult> ResetAsync(
            NativeRecoveryResetRequest request,
            CancellationToken cancellationToken = default)
        {
            ResetCount++;
            return Task.FromResult(
                new NativeRecoveryResetResult(Outcome));
        }
    }

    public sealed class SyntheticSettlementDirectoryVerifier : IDirectoryCredentialVerifier
    {
        public Guid ExpectedObjectId { get; set; }
        public int VerifyCalls { get; private set; }
        public Guid VerifiedObjectId { get; private set; }

        public Task<DirectoryCredentialVerificationResult> VerifyCredentialAsync(
            Guid directoryObjectId,
            string password,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifyCalls++;
            VerifiedObjectId = directoryObjectId;
            var authenticated = directoryObjectId == ExpectedObjectId &&
                string.Equals(password, SyntheticCurrentDirectoryCredential, StringComparison.Ordinal);
            return Task.FromResult(authenticated
                ? new DirectoryCredentialVerificationResult(
                    DirectoryCredentialOutcome.Authenticated,
                    new ManagedDirectoryIdentity(
                        directoryObjectId,
                        SettlementTargetIdentifier,
                        IsEligible: true,
                        IsEnabled: true,
                        IsLocked: false))
                : new DirectoryCredentialVerificationResult(DirectoryCredentialOutcome.InvalidCredentials));
        }
    }
}
