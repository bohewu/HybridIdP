using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Moq;
using Web.IdP.Controllers.Account;
using Xunit;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Events;
using Infrastructure;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using Web.IdP.Services;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class PasskeyControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletePasskey_ShouldPreserveOnlyTheAuthorizedCookie_WhenRemovalStampIsStillCurrent(
        bool supersededStamp)
    {
        var user = CreateEligibleUser("removal-cookie-user");
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        var original = _controller.HttpContext.User;
        var identity = (ClaimsIdentity)original.Identity!;
        identity.AddClaim(new Claim("amr", "mfa"));
        identity.AddClaim(new Claim("auth_time", "1770000000"));
        identity.AddClaim(new Claim(AuthorizationAuthenticationSession.AssuranceVersionClaim,
            AuthorizationAuthenticationSession.AssuranceVersion));
        var previousStamp = user.SecurityStamp;
        MfaEnrollmentSession.Begin(_session, user.Id, true, securityStamp: previousStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, original));
        var authentication = Mock.Get(_controller.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>());
        var properties = new AuthenticationProperties
        {
            IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20)
        };
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(original, properties, IdentityConstants.ApplicationScheme)));
        ClaimsPrincipal? refreshed = null;
        authentication.Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, principal, options) =>
            {
                refreshed = principal;
                Assert.Same(properties, options);
            }).Returns(Task.CompletedTask);
        _passkeyServiceMock.Setup(x => x.DeletePasskeyAsync(user.Id, 1, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                user.SecurityStamp = supersededStamp ? "independent-revocation" : "removal-stamp";
                await _dbContext.SaveChangesAsync();
                user.SecurityStamp = "removal-stamp";
                return MfaRemovalResult.Succeeded;
            });

        Assert.IsType<OkObjectResult>(await _controller.DeletePasskey(1, default));

        Assert.False(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        Assert.Equal(previousStamp, original.FindFirstValue("AspNet.Identity.SecurityStamp"));
        if (supersededStamp)
        {
            Assert.Null(refreshed);
            authentication.Verify(x => x.SignOutAsync(_controller.HttpContext, IdentityConstants.ApplicationScheme,
                It.IsAny<AuthenticationProperties>()), Times.Once);
            return;
        }
        Assert.NotNull(refreshed);
        Assert.Equal("removal-stamp", refreshed.FindFirstValue("AspNet.Identity.SecurityStamp"));
        Assert.Equal("1770000000", refreshed.FindFirstValue("auth_time"));
        Assert.False(MfaEnrollmentSession.HasMfa(refreshed));
        Assert.Null(refreshed.FindFirstValue(AuthorizationAuthenticationSession.MfaSecurityStampClaim));
        authentication.Verify(x => x.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
            It.IsAny<AuthenticationProperties>()), Times.Never);
        authentication.Verify(x => x.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme,
            It.IsAny<AuthenticationProperties>()), Times.Once);
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(refreshed, properties, IdentityConstants.ApplicationScheme)));
        _controller.HttpContext.User = refreshed;
        Assert.IsType<OkObjectResult>(await _controller.ListPasskeys(default));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.DeletePasskey(2, default)).StatusCode);
        authentication.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(original, properties, IdentityConstants.ApplicationScheme)));
        _controller.HttpContext.User = original;
        Assert.IsType<UnauthorizedObjectResult>(await _controller.ListPasskeys(default));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ListPasskeys_ShouldAllowOwnCompleteCookieWithoutManagementProof_AndKeepMutationsDenied(
        bool totp, bool email, bool hasPasskey)
    {
        var user = CreateEligibleUser("inventory-user");
        user.TwoFactorEnabled = totp;
        user.EmailMfaEnabled = email;
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        var keys = hasPasskey ? new List<UserCredentialDto> { new() { Id = 1, DeviceName = "Existing key" } } : [];
        _passkeyServiceMock.Setup(s => s.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys);

        var result = Assert.IsType<OkObjectResult>(await _controller.ListPasskeys(default));

        Assert.Same(keys, result.Value);
        Assert.False(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.DeletePasskey(1, default)).StatusCode);
        _passkeyServiceMock.Verify(s => s.DeletePasskeyAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _passkeyServiceMock.Verify(s => s.GetRegistrationOptionsAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoSuccessfulSignIn();
    }

    [Theory]
    [InlineData("retired")]
    [InlineData(null)]
    public async Task ListPasskeys_ShouldRequireSignIn_WhenCookieStampIsRetiredOrMissing(string? stamp)
    {
        var user = CreateEligibleUser("inventory-user");
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        user.SecurityStamp = stamp;

        var result = Assert.IsType<UnauthorizedObjectResult>(await _controller.ListPasskeys(default));

        Assert.Equal(401, result.StatusCode);
        Mock.Get(_controller.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>())
            .Verify(s => s.SignOutAsync(_controller.HttpContext, IdentityConstants.ApplicationScheme,
                It.IsAny<AuthenticationProperties>()), Times.Once);
        _passkeyServiceMock.Verify(s => s.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("password-change")]
    [InlineData("lifecycle")]
    [InlineData("migration")]
    [InlineData("different-subject")]
    public async Task ListPasskeys_ShouldNotReadInventory_WhenCompleteCookieCannotAccessAccount(string restriction)
    {
        var user = CreateEligibleUser("inventory-user");
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        if (restriction == "inactive") user.IsActive = false;
        if (restriction == "deleted") user.IsDeleted = true;
        if (restriction == "password-change") user.RequiresPasswordChange = true;
        if (restriction == "lifecycle") _lifecycleEligibilityMock.Setup(s => s.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (restriction == "migration") _migrationIssuanceGuardMock.Setup(s => s.CanIssueAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (restriction == "different-subject") user.Id = Guid.NewGuid();

        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.ListPasskeys(default)).StatusCode);
        _passkeyServiceMock.Verify(s => s.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListPasskeys_ShouldRequireSignIn_WhenNoApplicationOrEnrollmentCookieExists()
    {
        var user = CreateEligibleUser("inventory-user");
        ArrangeAuthenticatedUser(user);
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", user.Id.ToString()), new Claim("amr", "mfa")], "Bearer"));

        Assert.IsType<UnauthorizedObjectResult>(await _controller.ListPasskeys(default));
        _passkeyServiceMock.Verify(s => s.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListPasskeys_ShouldPreserveAuthorizedInitialEnrollmentAccess()
    {
        var user = CreateEligibleUser("initial-user");
        ArrangeAuthenticatedUser(user);
        ArrangeTwoFactorPartialAuthentication(user);

        Assert.IsType<OkObjectResult>(await _controller.ListPasskeys(default));
        Assert.False(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task Registration_ShouldRejectFreshEnrollmentProofAfterAccountStampChanges()
    {
        var user = CreateEligibleUser("stamp-revoked-enrollment");
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        MfaEnrollmentSession.Begin(_session, user.Id, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, _controller.HttpContext.User));
        user.SecurityStamp = "revoked-by-reset";
        _session.SetString("fido2.attestationOptions", "{}");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default)).StatusCode);
        _passkeyServiceMock.Verify(service => service.RegisterCredentialsAsync(It.IsAny<ApplicationUser>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Registration_ShouldRejectOldMfaCookieBeforeCreatingAnyNewFactor()
    {
        var user = CreateEligibleUser("old-mfa-cookie");
        user.TwoFactorEnabled = true;
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        ((ClaimsIdentity)_controller.HttpContext.User.Identity!).AddClaim(new Claim("amr", "mfa"));
        _session.SetString("fido2.attestationOptions", "{}");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default)).StatusCode);
        _passkeyServiceMock.Verify(service => service.RegisterCredentialsAsync(It.IsAny<ApplicationUser>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MakeCredentialOptions_ShouldAllowReplacementOnlyWhenExistingCredentialIsRetired(bool disabled)
    {
        var user = CreateEligibleUser("rebind-passkey-user");
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        MfaEnrollmentSession.Begin(_session, user.Id);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, _controller.HttpContext.User));
        _dbContext.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            CredentialId = new byte[] { 1 },
            PublicKey = new byte[] { 2 },
            DisabledAtUtc = disabled ? DateTime.UtcNow : null
        });
        await _dbContext.SaveChangesAsync();
        _securityPolicyServiceMock.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { EnablePasskey = true, MaxPasskeysPerUser = 1 });
        _passkeyServiceMock.Setup(s => s.GetRegistrationOptionsAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialCreateOptions
            {
                Challenge = new byte[] { 1 },
                Rp = new PublicKeyCredentialRpEntity("localhost", "HybridIdP"),
                User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.UserName, DisplayName = user.UserName },
                PubKeyCredParams = new List<PubKeyCredParam>(),
                AuthenticatorSelection = new AuthenticatorSelection()
            });

        var result = await _controller.MakeCredentialOptions(default);

        if (disabled)
        {
            var options = Assert.IsType<CredentialCreateOptions>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(Fido2NetLib.Objects.UserVerificationRequirement.Required, options.AuthenticatorSelection.UserVerification);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            _passkeyServiceMock.Verify(s => s.GetRegistrationOptionsAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        Assert.Single(_dbContext.UserCredentials);
    }

    [Fact]
    public async Task DeletePasskey_ShouldReturnAlternateSignInRequired_WhenServiceProtectsPrimaryKey()
    {
        var user = CreateEligibleUser("sole-primary-passkey-user");
        user.SecurityStamp = "current";
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        var identity = (ClaimsIdentity)_controller.HttpContext.User.Identity!;
        identity.AddClaim(new Claim("amr", "mfa"));
        identity.AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp));
        MfaEnrollmentSession.Begin(_session, user.Id, true, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, _controller.HttpContext.User));
        _securityPolicyServiceMock.Setup(value => value.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        _passkeyServiceMock.Setup(value => value.DeletePasskeyAsync(user.Id, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MfaRemovalResult.AlternateSignInRequired);
        var result = Assert.IsType<BadRequestObjectResult>(await _controller.DeletePasskey(1, default));
        Assert.Equal("alternateSignInRequired", result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
        Assert.Equal("current", user.SecurityStamp);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DeletePasskey_ShouldProtectLastActiveFactor_WhenRetiredCredentialRemains(int credentialId)
    {
        var user = CreateEligibleUser("last-active-passkey-user");
        user.SecurityStamp = "current-stamp";
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        ((ClaimsIdentity)_controller.HttpContext.User.Identity!).AddClaim(new Claim("amr", "mfa"));
        ((ClaimsIdentity)_controller.HttpContext.User.Identity!).AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp));
        MfaEnrollmentSession.Begin(_session, user.Id, requiresMfa: true, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, _controller.HttpContext.User));
        _passkeyServiceMock.Setup(s => s.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserCredentialDto { Id = 1 }]);
        _dbContext.UserCredentials.AddRange(
            new UserCredential { Id = 1, UserId = user.Id, CredentialId = new byte[] { 1 }, PublicKey = new byte[] { 2 } },
            new UserCredential { Id = 2, UserId = user.Id, CredentialId = new byte[] { 3 }, PublicKey = new byte[] { 4 }, DisabledAtUtc = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();
        _securityPolicyServiceMock.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { EnablePasskey = true, EnforceMandatoryMfaEnrollment = true });
        _passkeyServiceMock.Setup(s => s.DeletePasskeyAsync(user.Id, credentialId, It.IsAny<CancellationToken>())).ReturnsAsync(MfaRemovalResult.Succeeded);

        var result = await _controller.DeletePasskey(credentialId, default);

        if (credentialId == 1)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            _passkeyServiceMock.Verify(s => s.DeletePasskeyAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
            _passkeyServiceMock.Verify(s => s.DeletePasskeyAsync(user.Id, credentialId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task MakeCredential_ShouldRefreshVerifiedFirstEnrollmentForPasswordCookieAndAllowListing()
    {
        var user = CreateEligibleUser("first-passkey-cookie-user");
        ArrangeAuthenticatedUser(user);
        var cookie = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "pwd"),
             new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)], IdentityConstants.ApplicationScheme));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(() => AuthenticateResult.Success(new AuthenticationTicket(cookie, IdentityConstants.ApplicationScheme)));
        auth.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        _controller.HttpContext.RequestServices = new ServiceCollection().AddSingleton(auth.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
        MfaEnrollmentSession.Begin(_session, user.Id);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, cookie));
        _session.SetString("fido2.attestationOptions", "{}");
        _passkeyServiceMock.Setup(s => s.RegisterCredentialsAsync(user, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _passkeyServiceMock.Setup(s => s.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new UserCredentialDto { Id = 1, DeviceName = "key" }]))
            .ReturnsAsync((true, (string?)null, true));
        _signInManagerMock.Setup(s => s.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) =>
            {
                cookie = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!) }.Concat(claims), IdentityConstants.ApplicationScheme));
            }).Returns(Task.CompletedTask);
        Assert.IsType<OkObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default));
        Assert.True(MfaEnrollmentSession.HasMfa(cookie));
        Assert.IsType<OkObjectResult>(await _controller.ListPasskeys(default));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 10)]
    [InlineData(true, 0)]
    public async Task Registration_ShouldReserveInitialUvCompletionForNeverIssuedAccountBatch(bool hasBatch, int remaining)
    {
        var user = CreateEligibleUser("batch-enrollment");
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        ((ClaimsIdentity)_controller.HttpContext.User.Identity!).AddClaim(new Claim("amr", "mfa"));
        Mock.Get(_controller.HttpContext.RequestServices.GetRequiredService<IAccountRecoveryCodeService>())
            .Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountRecoveryCodeStatusDto(hasBatch, remaining));
        MfaEnrollmentSession.Begin(_session, user.Id, true, securityStamp: user.SecurityStamp);
        Assert.True(MfaEnrollmentSession.CompletePending(_session, _controller.HttpContext.User));
        _securityPolicyServiceMock.Setup(value => value.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy { EnablePasskey = true });
        _passkeyServiceMock.Setup(value => value.GetRegistrationOptionsAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialCreateOptions
            {
                Challenge = [1, 2, 3], Rp = new PublicKeyCredentialRpEntity("localhost", "Test"),
                User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.UserName, DisplayName = user.UserName },
                PubKeyCredParams = new List<PubKeyCredParam>(),
                AuthenticatorSelection = new AuthenticatorSelection { UserVerification = Fido2NetLib.Objects.UserVerificationRequirement.Preferred }
            });
        var options = Assert.IsType<CredentialCreateOptions>(Assert.IsType<OkObjectResult>(await _controller.MakeCredentialOptions(default)).Value);
        Assert.Equal(Fido2NetLib.Objects.UserVerificationRequirement.Required, options.AuthenticatorSelection.UserVerification);
        _passkeyServiceMock.Setup(value => value.RegisterCredentialsAsync(user, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyRegistrationResult(true, null, !hasBatch, hasBatch ? null : ["display-once"]));
        Assert.IsType<OkObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default));
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl);
        Assert.False(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        _signInManagerMock.Verify(value => value.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()),
            hasBatch ? Times.Never() : Times.Once());
    }

    [Fact]
    public async Task MakeCredential_ShouldDenyChallengeCookieBeforePersistingCredential()
    {
        var user = CreateEligibleUser("challenge-user");
        ArrangeAuthenticatedUser(user);
        ArrangeTwoFactorPartialAuthentication(user);
        user.TwoFactorEnabled = true;
        _session.SetString("fido2.attestationOptions", "{}");
        var result = await _controller.MakeCredential(EmptyClientResponse(), default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        _passkeyServiceMock.Verify(s => s.RegisterCredentialsAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoSuccessfulSignIn();
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("mandatory")]
    [InlineData("wrong-code")]
    [InlineData("wrong-purpose")]
    [InlineData("sign-in-only")]
    [InlineData("cross-user")]
    [InlineData("stale-stamp")]
    [InlineData("expired")]
    public async Task LastAccountCodeLogin_ShouldAuthorizeOnlyOnePurposeBoundReplacement(string condition)
    {
        var user = CreateEligibleUser("lost-last-key");
        _dbContext.Users.Add(user);
        _dbContext.UserCredentials.Add(new UserCredential { UserId = user.Id, CredentialId = [1], PublicKey = [2] });
        await _dbContext.SaveChangesAsync();
        var codes = new global::Infrastructure.Services.AccountRecoveryCodeService(_dbContext);
        var batch = await codes.GenerateAsync(user);
        Assert.NotNull(batch);
        foreach (var code in batch.Take(9)) Assert.True(await codes.RedeemAsync(user, code, "earlier-login"));
        Assert.Equal(1, (await codes.GetStatusAsync(user.Id)).RemainingCodes);
        ArrangeAuthenticatedUser(user);
        ArrangeApplicationCookieUser(user);
        var http = _controller.HttpContext;
        var authentication = Mock.Get(http.RequestServices.GetRequiredService<IAuthenticationService>());
        using var services = new ServiceCollection().AddSingleton(authentication.Object).AddSingleton(_userManagerMock.Object)
            .AddSingleton<IApplicationDbContext>(_dbContext).AddSingleton<IAccountRecoveryCodeService>(codes)
            .AddSingleton(_lifecycleEligibilityMock.Object).AddSingleton(_migrationIssuanceGuardMock.Object).BuildServiceProvider();
        http.RequestServices = services;
        _userManagerMock.Setup(value => value.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(value => value.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);
        _signInManagerMock.Setup(value => value.CanSignInAsync(user)).ReturnsAsync(true);
        _signInManagerMock.Object.Context = http;
        _securityPolicyServiceMock.Setup(value => value.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy
            { EnablePasskey = true, EnforceMandatoryMfaEnrollment = condition == "mandatory", MaxPasskeysPerUser = 10 });
        _passkeyServiceMock.Setup(value => value.GetUserPasskeysAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserCredentialDto { Id = 1 }]);
        _passkeyServiceMock.Setup(value => value.GetRegistrationOptionsAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialCreateOptions
            {
                Challenge = [1, 2, 3], Rp = new PublicKeyCredentialRpEntity("localhost", "Test"),
                User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.UserName, DisplayName = user.UserName },
                PubKeyCredParams = new List<PubKeyCredParam>(), AuthenticatorSelection = new AuthenticatorSelection()
            });
        _passkeyServiceMock.Setup(value => value.RegisterCredentialsAsync(user, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyRegistrationResult(true, null, true));
        if (condition == "mandatory")
        {
            http.User = new ClaimsPrincipal();
            authentication.Setup(value => value.AuthenticateAsync(http, IdentityConstants.ApplicationScheme)).ReturnsAsync(AuthenticateResult.NoResult());
            authentication.Setup(value => value.AuthenticateAsync(http, IdentityConstants.TwoFactorUserIdScheme))
                .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(TwoFactorAuthenticationSession.CreateIdentity(user, _userManagerMock.Object)), IdentityConstants.TwoFactorUserIdScheme)));
        }
        // Even a valid receipt for a different purpose cannot mint replacement authority.
        Assert.False(MfaEnrollmentSession.TryBeginRecoveryEnrollment(http, user));
        var localizer = new Mock<IStringLocalizer<global::Web.IdP.SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
        List<Claim>? issued = null;
        _signInManagerMock.Setup(value => value.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) =>
            {
                issued = claims.ToList();
                var principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)], IdentityConstants.ApplicationScheme));
                ((ClaimsIdentity)principal.Identity!).AddClaims(issued);
                http.User = principal;
                authentication.Setup(value => value.AuthenticateAsync(http, IdentityConstants.ApplicationScheme))
                    .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
                authentication.Setup(value => value.AuthenticateAsync(http, IdentityConstants.TwoFactorUserIdScheme)).ReturnsAsync(AuthenticateResult.NoResult());
            }).Returns(Task.CompletedTask);
        var returnUrl = condition == "mandatory" ? "/connect/authorize?client_id=test&acr_values=mfa" : "/Account/Profile";
        var page = new LoginMfaModel(_signInManagerMock.Object, _userManagerMock.Object, Mock.Of<IMfaService>(),
            _userManagementServiceMock.Object, _passkeyServiceMock.Object, Mock.Of<IDomainEventPublisher>(),
            Mock.Of<ILogger<LoginMfaModel>>(), localizer.Object, _migrationIssuanceGuardMock.Object, _lifecycleEligibilityMock.Object)
        {
            PageContext = new PageContext(new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor())), ReturnUrl = returnUrl,
            Method = "recovery",
            Input = new LoginMfaModel.InputModel
            {
                RecoveryCode = condition == "wrong-code" ? new string('0', 32) : batch[9],
                RecoveryPurpose = condition is "wrong-purpose" or "sign-in-only" ? condition : MfaEnrollmentSession.RecoveryEnrollmentPurpose
            }
        };
        var result = await page.OnPostAsync();
        if (condition is "wrong-code" or "wrong-purpose" or "sign-in-only")
        {
            if (condition == "wrong-code")
            {
                Assert.IsType<PageResult>(result);
                Assert.Null(issued);
                Assert.Equal(1, (await codes.GetStatusAsync(user.Id)).RemainingCodes);
            }
            else
            {
                Assert.Equal(returnUrl, Assert.IsType<RedirectResult>(result).Url);
                Assert.Equal(0, (await codes.GetStatusAsync(user.Id)).RemainingCodes);
            }
            Assert.False(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
            Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
            return;
        }
        var setupRedirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./MfaSetup", setupRedirect.PageName);
        Assert.Equal(returnUrl, setupRedirect.RouteValues!["returnUrl"]);
        Assert.Equal(new[] { "mfa" }, issued!.Select(claim => claim.Value));
        Assert.Equal(0, (await codes.GetStatusAsync(user.Id)).RemainingCodes);
        Assert.True(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        Assert.False(await MfaEnrollmentSession.IsRemovalAuthorizedAsync(http, user));
        Assert.False(MfaEnrollmentSession.TryBeginRecoveryEnrollment(http, user));

        if (condition == "cross-user")
        {
            var other = CreateEligibleUser("other-user");
            ArrangeAuthenticatedUser(other);
            _passkeyServiceMock.Setup(value => value.GetUserPasskeysAsync(other.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var identity = (ClaimsIdentity)http.User.Identity!;
            identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier)!);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, other.Id.ToString()));
            _userManagerMock.Setup(value => value.GetUserAsync(http.User)).ReturnsAsync(other);
        }
        else if (condition == "stale-stamp")
        {
            user.SecurityStamp = "new-stamp";
            var identity = (ClaimsIdentity)http.User.Identity!;
            identity.RemoveClaim(identity.FindFirst("AspNet.Identity.SecurityStamp")!);
            identity.AddClaim(new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp));
        }
        else if (condition == "expired")
        {
            var proof = System.Text.Json.Nodes.JsonNode.Parse(_session.GetString("MfaEnrollment:Proof")!)!;
            proof["ExpiresUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            _session.SetString("MfaEnrollment:Proof", proof.ToJsonString());
        }
        if (condition is "cross-user" or "stale-stamp" or "expired")
        {
            Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
            Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default)).StatusCode);
            _passkeyServiceMock.Verify(value => value.RegisterCredentialsAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            return;
        }
        var setup = new MfaSetupModel(_signInManagerMock.Object, _userManagerMock.Object, localizer.Object,
            _securityPolicyServiceMock.Object, _migrationIssuanceGuardMock.Object, _lifecycleEligibilityMock.Object, _passkeyServiceMock.Object)
            { PageContext = page.PageContext, ReturnUrl = returnUrl };
        Assert.IsType<PageResult>(await setup.OnGetAsync());
        Assert.Equal(returnUrl, setup.ReturnUrl);
        Assert.IsType<OkObjectResult>(await _controller.MakeCredentialOptions(default));
        var registered = Assert.IsType<OkObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default));
        Assert.Null(registered.Value!.GetType().GetProperty("accountRecoveryCodes"));
        Assert.False(MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default)).StatusCode);
        Assert.False(MfaEnrollmentSession.TryBeginRecoveryEnrollment(http, user));
        _signInManagerMock.Verify(value => value.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()), Times.Once);
        _passkeyServiceMock.Verify(value => value.RegisterCredentialsAsync(user, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PasskeyManagement_ShouldRejectExistingFactorChallengeForListDeleteAndOptions()
    {
        var user = CreateEligibleUser("challenge-user");
        ArrangeAuthenticatedUser(user);
        ArrangeTwoFactorPartialAuthentication(user);
        user.EmailMfaEnabled = true;
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.ListPasskeys(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.DeletePasskey(1, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.MakeCredentialOptions(default)).StatusCode);
        _passkeyServiceMock.Verify(s => s.DeletePasskeyAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _passkeyServiceMock.Verify(s => s.GetRegistrationOptionsAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MakeCredential_ShouldNotClaimMfaWithoutVerifiedRegistration()
    {
        var user = CreateEligibleUser("initial-user");
        ArrangeAuthenticatedUser(user);
        ArrangeTwoFactorPartialAuthentication(user);
        _session.SetString("fido2.attestationOptions", "{}");
        _passkeyServiceMock.Setup(s => s.RegisterCredentialsAsync(user, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null, false));
        Assert.IsType<BadRequestObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default));
        Assert.False(MfaEnrollmentSession.HasInitial(_session, user.Id));
        VerifyNoSuccessfulSignIn();
    }

    private readonly Mock<IPasskeyService> _passkeyServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly Mock<IUserManagementService> _userManagementServiceMock;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<ILogger<PasskeyController>> _loggerMock;
    private readonly Mock<IMigrationIssuanceGuard> _migrationIssuanceGuardMock;
    private readonly Mock<ICurrentUserLifecycleEligibility> _lifecycleEligibilityMock;
    private readonly MemorySession _session;
    private readonly PasskeyController _controller;

    public PasskeyControllerTests()
    {
        _passkeyServiceMock = new Mock<IPasskeyService>();
        _passkeyServiceMock.Setup(service => service.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null, null, null, null, null, null, null, null);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object, contextAccessorMock.Object, claimsFactoryMock.Object, null, null, null, null);
        _userManagerMock
            .Setup(manager => manager.IsLockedOutAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(false);
        _signInManagerMock
            .Setup(manager => manager.CanSignInAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(true);

        _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
        _securityPolicyServiceMock
            .Setup(service => service.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { EnablePasskey = true });
        _securityPolicyServiceMock
            .Setup(service => service.GetCurrentPolicyForPasskeyAuthenticationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityPolicy { EnablePasskey = true });
        _userManagementServiceMock = new Mock<IUserManagementService>();
        
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _auditServiceMock = new Mock<IAuditService>();
        _auditServiceMock
            .Setup(service => service.LogEventAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _loggerMock = new Mock<ILogger<PasskeyController>>();
        _migrationIssuanceGuardMock = new Mock<IMigrationIssuanceGuard>();
        _migrationIssuanceGuardMock
            .Setup(service => service.CanIssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lifecycleEligibilityMock = new Mock<ICurrentUserLifecycleEligibility>();
        _lifecycleEligibilityMock
            .Setup(service => service.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        _session = new MemorySession();
        var httpContext = new DefaultHttpContext();
        httpContext.Session = _session;
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();

        _controller = new PasskeyController(
            _passkeyServiceMock.Object,
            _signInManagerMock.Object,
            _userManagerMock.Object,
            _securityPolicyServiceMock.Object,
            _userManagementServiceMock.Object,
            _dbContext,
            _auditServiceMock.Object,
            _loggerMock.Object,
            _migrationIssuanceGuardMock.Object,
            _lifecycleEligibilityMock.Object
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task MakeAssertion_SuspendedPerson_ReturnsBadRequest()
    {
        // Arrange
        var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Suspended };
        var user = new ApplicationUser
        {
            PersonId = person.Id,
            Person = person,
            IsActive = true
        };
        
        // Mock session data
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"123\"}");

        _passkeyServiceMock.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, true, (string?)null));

        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _controller.MakeAssertion(clientResponse, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var val = badRequest.Value; 
        // Use reflection or dynamic to check error property? Or just check type.
        // Assuming implementation returns new { success = false, error = "Account not active" }
        // Using dynamic for simplicity in test
        var data = badRequest.Value!;
        var success = (bool?)data.GetType().GetProperty("success")?.GetValue(data);
        var error = (string?)data.GetType().GetProperty("error")?.GetValue(data);
        
        Assert.False(success);
        Assert.Equal("Account not active", error);
        
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_DeactivatedUser_ReturnsBadRequest()
    {
        // Arrange
        var person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
        var user = new ApplicationUser
        {
            PersonId = person.Id,
            Person = person,
            IsActive = false
        };
        
        // Mock session data
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"123\"}");

        _passkeyServiceMock.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, true, (string?)null));

        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _controller.MakeAssertion(clientResponse, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var data = badRequest.Value!;
        var success = (bool?)data.GetType().GetProperty("success")?.GetValue(data);
        var error = (string?)data.GetType().GetProperty("error")?.GetValue(data);

        Assert.False(success);
        Assert.Equal("User account deactivated", error);

        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_DeletedUser_ReturnsBadRequestWithoutSigningIn()
    {
        var user = new ApplicationUser
        {
            UserName = "deleted-user",
            IsActive = true,
            IsDeleted = true
        };
        user.Person = new Person { Id = Guid.NewGuid(), Status = PersonStatus.Active };
        user.PersonId = user.Person.Id;
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"123\"}");
        _passkeyServiceMock
            .Setup(service => service.VerifyAssertionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, true, (string?)null));
        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        var result = await _controller.MakeAssertion(
            clientResponse,
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNoSuccessfulSignIn();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MakeAssertion_MissingPersonLinkOrRecord_ReturnsBadRequestWithoutSigningIn(
        bool hasMissingPersonId)
    {
        var user = new ApplicationUser
        {
            UserName = "orphan-user",
            PersonId = hasMissingPersonId ? Guid.NewGuid() : null,
            IsActive = true
        };
        ArrangeVerifiedAssertion(user);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_PersonOutsideLifecycleDates_ReturnsBadRequestWithoutSigningIn()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Status = PersonStatus.Active,
            EndDate = DateTime.UtcNow.AddDays(-1)
        };
        var user = new ApplicationUser
        {
            UserName = "ended-person-user",
            PersonId = person.Id,
            Person = person,
            IsActive = true
        };
        ArrangeVerifiedAssertion(user);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_LockedOutUser_ReturnsBadRequestWithoutSigningIn()
    {
        var user = CreateEligibleUser("locked-user");
        ArrangeVerifiedAssertion(user);
        _userManagerMock
            .Setup(manager => manager.IsLockedOutAsync(user))
            .ReturnsAsync(true);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_IdentityPolicyDenied_ReturnsBadRequestWithoutSigningIn()
    {
        var user = CreateEligibleUser("identity-denied-user");
        ArrangeVerifiedAssertion(user);
        _signInManagerMock
            .Setup(manager => manager.CanSignInAsync(user))
            .ReturnsAsync(false);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_EnabledPasskeyWithUserVerification_ReturnsOkAndSignsInWithMfaAmr()
    {
        // Arrange
        var user = CreateEligibleUser("testuser");
        
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"123\"}");

        _passkeyServiceMock.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, true, (string?)null));

        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _controller.MakeAssertion(clientResponse, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var data = okResult.Value!;
        var success = (bool?)data.GetType().GetProperty("success")?.GetValue(data);
        var username = (string?)data.GetType().GetProperty("username")?.GetValue(data);

        Assert.True(success);
        Assert.Equal("testuser", username);

        // Verify SignIn WAS called with [hwk, user, mfa] AMR claims
        _signInManagerMock.Verify(x => x.SignInWithClaimsAsync(user, false, It.Is<IEnumerable<Claim>>(c => 
            c.Any(claim => claim.Type == "amr" && claim.Value == Core.Domain.Constants.AuthConstants.Amr.HardwareKey) &&
            c.Any(claim => claim.Type == "amr" && claim.Value == Core.Domain.Constants.AuthConstants.Amr.UserPresence) &&
            c.Any(claim => claim.Type == "amr" && claim.Value == Core.Domain.Constants.AuthConstants.Amr.Mfa)
        )), Times.Once);
        _userManagementServiceMock.Verify(
            service => service.UpdateLastLoginAsync(
                user.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MakeAssertion_WhenCurrentLifecycleIsIneligible_DeniesBeforeFullCookie()
    {
        var user = CreateEligibleUser("stale-lifecycle-user");
        ArrangeVerifiedAssertion(user);
        _lifecycleEligibilityMock
            .Setup(service => service.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        var failure = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "Authentication failed",
            failure.Value!.GetType().GetProperty("error")?.GetValue(failure.Value));
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeCredential_WhenMigrationIsIncomplete_DeniesBeforeFullCookie()
    {
        var user = CreateEligibleUser("partial-registration-user");
        ArrangeAuthenticatedUser(user);
        ArrangeTwoFactorPartialAuthentication(user);
        _session.SetString("fido2.attestationOptions", "{\"challenge\":\"123\"}");
        _passkeyServiceMock
            .Setup(service => service.RegisterCredentialsAsync(
                user,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null, true));
        _migrationIssuanceGuardMock
            .Setup(service => service.CanIssueAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.MakeCredential(
            EmptyClientResponse(),
            CancellationToken.None);

        var failure = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "Authentication failed",
            failure.Value!.GetType().GetProperty("error")?.GetValue(failure.Value));
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeCredential_WithEligibleLifecycleAndFinalizedMigration_IssuesFullCookie()
    {
        var user = CreateEligibleUser("partial-registration-user");
        ArrangeAuthenticatedUser(user);
        ArrangeTwoFactorPartialAuthentication(user);
        _session.SetString("fido2.attestationOptions", "{\"challenge\":\"123\"}");
        _passkeyServiceMock
            .Setup(service => service.RegisterCredentialsAsync(
                user,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null, true));
        _signInManagerMock
            .Setup(manager => manager.SignInWithClaimsAsync(
                user,
                false,
                It.IsAny<IEnumerable<Claim>>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.MakeCredential(
            EmptyClientResponse(),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _signInManagerMock.Verify(
            manager => manager.SignInWithClaimsAsync(
                user,
                false,
                It.IsAny<IEnumerable<Claim>>()),
            Times.Once);
    }

    [Fact]
    public async Task MakeAssertion_PasskeyDisabled_ReturnsForbiddenWithoutVerifyingOrSigningIn()
    {
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"123\"}");
        _securityPolicyServiceMock
            .Setup(service => service.GetCurrentPolicyForPasskeyAuthenticationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityPolicy { EnablePasskey = false });

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var error = (string?)forbidden.Value!.GetType().GetProperty("error")?.GetValue(forbidden.Value);
        Assert.Equal("Passkey authentication is disabled", error);
        _securityPolicyServiceMock.Verify(
            service => service.GetCurrentPolicyForPasskeyAuthenticationAsync(CancellationToken.None),
            Times.Once);
        _securityPolicyServiceMock.Verify(
            service => service.GetCurrentPolicyAsync(),
            Times.Never);
        _passkeyServiceMock.Verify(
            service => service.VerifyAssertionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoSuccessfulSignIn();
        Assert.Empty(_auditServiceMock.Invocations);
    }

    [Fact]
    public async Task MakeAssertion_EnabledPasskeyWithoutUserVerification_OmitsMfaAmr()
    {
        var user = CreateEligibleUser("user-presence-only");
        ArrangeVerifiedAssertion(user, userVerified: false);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _signInManagerMock.Verify(manager => manager.SignInWithClaimsAsync(
            user,
            false,
            It.Is<IEnumerable<Claim>>(claims =>
                claims.Any(claim => claim.Type == "amr" && claim.Value == AuthConstants.Amr.HardwareKey) &&
                claims.Any(claim => claim.Type == "amr" && claim.Value == AuthConstants.Amr.UserPresence) &&
                !claims.Any(claim => claim.Type == "amr" && claim.Value == AuthConstants.Amr.Mfa))),
            Times.Once);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("totp")]
    public async Task MakeAssertion_PresenceOnly_ShouldContinueThroughAdditionalFactor_WithOriginalAuthorizationRequest(string method)
    {
        var user = CreateEligibleUser("presence-only-step-up");
        user.TwoFactorEnabled = true;
        user.EmailMfaEnabled = true;
        user.Email = "step-up@example.test";
        _userManagerMock.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        ArrangeVerifiedAssertion(user, userVerified: false);
        var http = _controller.HttpContext;
        var authentication = Mock.Get(http.RequestServices.GetRequiredService<IAuthenticationService>());
        ClaimsPrincipal? partial = null;
        authentication.Setup(service => service.SignInAsync(http, IdentityConstants.TwoFactorUserIdScheme,
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, principal, _) => partial = principal)
            .Returns(Task.CompletedTask);
        authentication.Setup(service => service.AuthenticateAsync(http, IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(() => partial == null ? AuthenticateResult.NoResult() :
                AuthenticateResult.Success(new AuthenticationTicket(partial, IdentityConstants.TwoFactorUserIdScheme)));
        var issuedMethods = new List<string[]>();
        _signInManagerMock.Setup(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) =>
            {
                var methods = claims.ToArray();
                issuedMethods.Add(methods.Select(claim => claim.Value).ToArray());
                http.User = new ClaimsPrincipal(new ClaimsIdentity(
                    TwoFactorAuthenticationSession.CreateIdentity(user, _userManagerMock.Object).Claims.Concat(methods),
                    IdentityConstants.ApplicationScheme));
            }).Returns(Task.CompletedTask);

        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));

        Assert.Equal(new[] { "hwk", "user" }, Assert.Single(issuedMethods));
        Assert.False(MfaEnrollmentSession.HasMfa(http.User));
        Assert.NotNull(partial);
        Assert.Equal(user.Id.ToString(), partial.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(user.SecurityStamp, partial.FindFirstValue(_userManagerMock.Object.Options.ClaimsIdentity.SecurityStampClaimType));
        Assert.False(AuthenticationMethodSession.HasFreshPassword(_session, user));
        Assert.Equal(new[] { "hwk", "user" }, AuthenticationMethodSession.Get(_session, user));
        using var pending = System.Text.Json.JsonDocument.Parse(_session.GetString(AuthenticationMethodSession.SessionKey)!);
        Assert.False(pending.RootElement.GetProperty("PasswordVerified").GetBoolean());

        const string returnUrl = "/connect/authorize?client_id=testclient-public&response_type=code&scope=openid%20profile&redirect_uri=https%3A%2F%2Fclient.example.test%2Fsignin-oidc&state=original-state&nonce=original-nonce&code_challenge=original-challenge&code_challenge_method=S256&acr_values=mfa";
        var mfa = new Mock<IMfaService>();
        mfa.Setup(service => service.ValidateTotpCodeAsync(user, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mfa.Setup(service => service.VerifyEmailMfaCodeAsync(user, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var localizer = Mock.Of<IStringLocalizer<global::Web.IdP.SharedResource>>();
        var events = Mock.Of<IDomainEventPublisher>();
        var selector = new LoginMfaModel(_signInManagerMock.Object, _userManagerMock.Object, mfa.Object,
            _userManagementServiceMock.Object, _passkeyServiceMock.Object, events, Mock.Of<ILogger<LoginMfaModel>>(),
            localizer, _migrationIssuanceGuardMock.Object, _lifecycleEligibilityMock.Object)
            { PageContext = new PageContext { HttpContext = http } };
        Assert.IsType<PageResult>(await selector.OnGetAsync(returnUrl));
        Assert.Equal(returnUrl, selector.ReturnUrl);

        IActionResult completion;
        if (method == "email")
        {
            var page = new LoginEmailOtpModel(_signInManagerMock.Object, _userManagerMock.Object, mfa.Object,
                _userManagementServiceMock.Object, events, Mock.Of<ILogger<LoginEmailOtpModel>>(), localizer,
                _migrationIssuanceGuardMock.Object, _lifecycleEligibilityMock.Object)
                { PageContext = new PageContext { HttpContext = http }, Input = new() { EmailCode = "123456" } };
            Assert.IsType<PageResult>(await page.OnGetAsync(selector.ReturnUrl));
            Assert.Equal(returnUrl, page.ReturnUrl);
            completion = await page.OnPostAsync();
        }
        else
        {
            var page = new LoginTotpModel(_signInManagerMock.Object, _userManagerMock.Object, mfa.Object,
                _userManagementServiceMock.Object, events, Mock.Of<ILogger<LoginTotpModel>>(), localizer,
                _migrationIssuanceGuardMock.Object, _lifecycleEligibilityMock.Object)
                { PageContext = new PageContext { HttpContext = http }, Input = new() { TotpCode = "123456" } };
            Assert.IsType<PageResult>(await page.OnGetAsync(selector.ReturnUrl));
            Assert.Equal(returnUrl, page.ReturnUrl);
            completion = await page.OnPostAsync();
        }

        Assert.Equal(returnUrl, Assert.IsType<RedirectResult>(completion).Url);
        Assert.Equal(2, issuedMethods.Count);
        Assert.Equal(new[] { "hwk", "user", "mfa", "otp" }, issuedMethods[1]);
        Assert.True(MfaEnrollmentSession.HasMfa(http.User));
        Assert.DoesNotContain(AuthenticationMethodSession.SessionKey, _session.Keys);
    }

    [Fact]
    public async Task MakeAssertion_PresenceOnly_ShouldPreserveExistingTemporaryCookieNonceAndExpiry()
    {
        var user = CreateEligibleUser("existing-partial");
        ArrangeTwoFactorPartialAuthentication(user);
        var http = _controller.HttpContext;
        var authentication = Mock.Get(http.RequestServices.GetRequiredService<IAuthenticationService>());
        var partial = (await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme)).Principal!;
        var purpose = partial.FindFirstValue(MfaEnrollmentSession.InitialPurposeClaim);
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1) };
        authentication.Setup(service => service.AuthenticateAsync(http, IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(partial, properties, IdentityConstants.TwoFactorUserIdScheme)));
        ArrangeVerifiedAssertion(user, userVerified: false);
        _session.SetString("fido2.assertionUserId", user.Id.ToString());

        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));

        var retained = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        Assert.Same(properties, retained.Properties);
        Assert.Equal(purpose, retained.Principal!.FindFirstValue(MfaEnrollmentSession.InitialPurposeClaim));
        authentication.Verify(service => service.SignInAsync(http, IdentityConstants.TwoFactorUserIdScheme,
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Never);
        authentication.Verify(service => service.SignOutAsync(http, IdentityConstants.TwoFactorUserIdScheme,
            It.IsAny<AuthenticationProperties>()), Times.Never);
        Assert.Equal(new[] { "hwk", "user" }, AuthenticationMethodSession.Get(_session, user));
    }

    [Fact]
    public async Task AssertionOptionsPost_AuthenticatedStepUp_UsesApplicationCookieUser()
    {
        var stepUpUser = CreateEligibleUser("step-up-user");
        ArrangeApplicationCookieUser(stepUpUser);
        _passkeyServiceMock
            .Setup(service => service.GetAssertionOptionsAsync(
                stepUpUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AssertionOptions.FromJson(
                "{\"challenge\":\"MTIz\",\"timeout\":60000,\"rpId\":\"localhost\",\"allowCredentials\":[],\"userVerification\":\"preferred\"}"));

        var result = await _controller.AssertionOptionsPost(
            new LoginOptionsRequest("different-user"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _passkeyServiceMock.Verify(service => service.GetAssertionOptionsAsync(
            stepUpUser.Id,
            CancellationToken.None), Times.Once);
        _passkeyServiceMock.Verify(service => service.GetAssertionOptionsAsync(
            It.Is<Guid?>(id => id != stepUpUser.Id),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MakeAssertion_ShouldCombineFreshPasswordWithBoundKey_AndConsumeTemporaryProof(bool userVerified)
    {
        var user = CreateEligibleUser("password-and-key");
        ArrangeTwoFactorPartialAuthentication(user);
        AuthenticationMethodSession.Replace(_session, user, AuthConstants.Amr.Password);
        var authenticationTime = AuthenticationMethodSession.GetAuthenticationTime(_session, user);
        ArrangeVerifiedAssertion(user, userVerified);
        _session.SetString("fido2.assertionUserId", user.Id.ToString());
        _signInManagerMock.Setup(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) =>
            {
                Assert.Equal(new[] { "pwd", "hwk", "user", "mfa" }, claims.Select(claim => claim.Value));
                Assert.Equal(authenticationTime, AuthenticationMethodSession.GetAuthenticationTime(_session, user));
            }).Returns(Task.CompletedTask);

        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));

        Assert.DoesNotContain(AuthenticationMethodSession.SessionKey, _session.Keys);
        Assert.DoesNotContain("fido2.assertionOptions", _session.Keys);
        Assert.DoesNotContain("fido2.assertionUserId", _session.Keys);
        Mock.Get(_controller.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>())
            .Verify(service => service.SignOutAsync(_controller.HttpContext, IdentityConstants.TwoFactorUserIdScheme,
                It.IsAny<AuthenticationProperties>()), Times.Once);
        Assert.IsType<BadRequestObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
        _signInManagerMock.Verify(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()), Times.Once);

        // Even if the old protected temporary cookie is replayed, consumed server proof cannot add MFA.
        ArrangeVerifiedAssertion(user, userVerified: false);
        _session.SetString("fido2.assertionUserId", user.Id.ToString());
        _signInManagerMock.Setup(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) =>
                Assert.Equal(new[] { "hwk", "user" }, claims.Select(claim => claim.Value)))
            .Returns(Task.CompletedTask);
        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("subject")]
    [InlineData("stamp")]
    [InlineData("legacy")]
    [InlineData("external")]
    [InlineData("imported-pwd")]
    [InlineData("unbound-options")]
    [InlineData("old-application-cookie")]
    public async Task MakeAssertion_ShouldNotAddMfa_WhenPasswordEvidenceIsNotFreshAndBound(string condition)
    {
        var user = CreateEligibleUser("presence-only-key");
        ArrangeTwoFactorPartialAuthentication(user);
        AuthenticationMethodSession.Replace(_session, user, AuthConstants.Amr.Password);
        if (condition == "absent") AuthenticationMethodSession.Consume(_session);
        if (condition == "external") AuthenticationMethodSession.Replace(_session, user, AuthConstants.Amr.External);
        if (condition == "imported-pwd")
        {
            AuthenticationMethodSession.Consume(_session);
            AuthenticationMethodSession.Add(_session, user, AuthConstants.Amr.Password);
        }
        if (condition is "expired" or "future" or "subject" or "stamp" or "legacy")
        {
            var pending = System.Text.Json.Nodes.JsonNode.Parse(_session.GetString(AuthenticationMethodSession.SessionKey)!)!;
            if (condition == "expired") pending["ExpiresUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (condition == "future") pending["AuthenticatedAt"] = DateTimeOffset.UtcNow.AddMinutes(1);
            if (condition == "subject") pending["UserId"] = Guid.NewGuid();
            if (condition == "stamp") pending["SecurityStamp"] = "retired-stamp";
            if (condition == "legacy") pending.AsObject().Remove("PasswordVerified");
            _session.SetString(AuthenticationMethodSession.SessionKey, pending.ToJsonString());
        }
        if (condition == "old-application-cookie")
        {
            ArrangeApplicationCookieUser(user);
            ((ClaimsIdentity)_controller.HttpContext.User.Identity!).AddClaim(new Claim("amr", "pwd"));
        }
        ArrangeVerifiedAssertion(user, userVerified: false);
        if (condition != "unbound-options") _session.SetString("fido2.assertionUserId", user.Id.ToString());

        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));

        _signInManagerMock.Verify(manager => manager.SignInWithClaimsAsync(user, false,
            It.Is<IEnumerable<Claim>>(claims => claims.Select(claim => claim.Value).SequenceEqual(new[] { "hwk", "user" }))), Times.Once);
        Assert.False(AuthenticationMethodSession.HasFreshPassword(_session, user));
        Assert.Equal(new[] { "hwk", "user" }, AuthenticationMethodSession.Get(_session, user));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MakeAssertion_ShouldDenyBoundPasswordCeremony_WhenTemporaryCookieStampChanged(bool missingStamp)
    {
        var user = CreateEligibleUser("changed-password-stamp");
        ArrangeTwoFactorPartialAuthentication(user);
        AuthenticationMethodSession.Replace(_session, user, AuthConstants.Amr.Password);
        ArrangeVerifiedAssertion(user, userVerified: false);
        _session.SetString("fido2.assertionUserId", user.Id.ToString());
        user.SecurityStamp = missingStamp ? null : "new-stamp";

        Assert.IsType<BadRequestObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));

        _passkeyServiceMock.Verify(service => service.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _signInManagerMock.Verify(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<IEnumerable<Claim>>()), Times.Never);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("new-key", false)]
    [InlineData("expired-operation", false)]
    [InlineData("wrong-operation-stamp", false)]
    [InlineData("no-password", false)]
    public async Task MakeAssertion_ShouldGrantEnrollmentProof_OnlyForQualifiedMfaAndBoundOperation(string condition, bool expected)
    {
        var user = CreateEligibleUser("enrollment-reauth");
        ArrangeTwoFactorPartialAuthentication(user);
        var authentication = _controller.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication).AddSingleton(_userManagerMock.Object)
            .AddSingleton<IApplicationDbContext>(_dbContext).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
        _controller.HttpContext.RequestServices = services;
        _dbContext.UserCredentials.Add(new UserCredential { UserId = user.Id, CredentialId = [1], PublicKey = [2] });
        await _dbContext.SaveChangesAsync();
        await AccountSecurityOperationSession.BeginAsync(_controller.HttpContext, user,
            AccountSecurityOperationSession.MfaEnrollmentPurpose, user.Id.ToString());
        MfaEnrollmentSession.Begin(_session, user.Id, true, securityStamp: user.SecurityStamp);
        if (condition != "no-password") AuthenticationMethodSession.Replace(_session, user, AuthConstants.Amr.Password);
        if (condition is "expired-operation" or "wrong-operation-stamp")
        {
            var operation = System.Text.Json.Nodes.JsonNode.Parse(_session.GetString("account-security.operation")!)!;
            if (condition == "expired-operation") operation["ExpiresUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            else operation["Stamp"] = "old-stamp";
            _session.SetString("account-security.operation", operation.ToJsonString());
        }
        ArrangeVerifiedAssertion(user, userVerified: false);
        _session.SetString("fido2.assertionUserId", user.Id.ToString());
        _signInManagerMock.Setup(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()))
            .Callback<ApplicationUser, bool, IEnumerable<Claim>>((_, _, claims) =>
            {
                var principal = new ClaimsPrincipal(TwoFactorAuthenticationSession.CreateIdentity(user, _userManagerMock.Object));
                ((ClaimsIdentity)principal.Identity!).AddClaims(claims);
                Assert.Equal(expected, MfaEnrollmentSession.CompletePending(_controller.HttpContext, principal));
            }).Returns(Task.CompletedTask);
        using var response = System.Text.Json.JsonDocument.Parse(condition == "new-key" ? "{\"id\":\"Ag\"}" : "{\"id\":\"AQ\"}");

        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(response.RootElement, default));

        Assert.Equal(expected, MfaEnrollmentSession.HasFreshProof(_session, user.Id));
        if (expected)
            Assert.Null(AccountSecurityOperationSession.GetNonce(_controller.HttpContext,
                AccountSecurityOperationSession.MfaEnrollmentPurpose, user.Id.ToString()));
    }

    [Theory]
    [InlineData("known-user")]
    [InlineData("unknown-user")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AssertionOptionsPost_AnonymousRequest_ShouldIgnoreSubmittedUsername(string? username)
    {
        _passkeyServiceMock.Setup(service => service.GetAssertionOptionsAsync(
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AssertionOptions.FromJson("{\"challenge\":\"AQ\",\"allowCredentials\":[]}"));

        var result = await _controller.AssertionOptionsPost(new LoginOptionsRequest(username), default);

        Assert.IsType<OkObjectResult>(result);
        _passkeyServiceMock.Verify(service => service.GetAssertionOptionsAsync(null, default), Times.Once);
        Assert.DoesNotContain("fido2.assertionUserId", _session.Keys);
    }

    [Fact]
    public async Task AssertionOptionsPost_PartialCookie_ShouldUseProtectedSubjectForHistoricalKeys()
    {
        var user = CreateEligibleUser("first-factor-user");
        ArrangeTwoFactorPartialAuthentication(user);
        _passkeyServiceMock.Setup(service => service.GetAssertionOptionsAsync(user.Id, default))
            .ReturnsAsync(AssertionOptions.FromJson("{\"challenge\":\"AQ\",\"allowCredentials\":[]}"));

        Assert.IsType<OkObjectResult>(await _controller.AssertionOptionsPost(new LoginOptionsRequest("other-user"), default));
        _passkeyServiceMock.Verify(service => service.GetAssertionOptionsAsync(user.Id, default), Times.Once);
        Assert.Equal(user.Id.ToString(), _session.GetString("fido2.assertionUserId"));
    }

    [Fact]
    public async Task AssertionOptionsPost_UnrelatedBearer_ShouldNotResolveSubmittedOrClaimedAccount()
    {
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));
        _passkeyServiceMock.Setup(service => service.GetAssertionOptionsAsync(null, default))
            .ReturnsAsync(AssertionOptions.FromJson("{\"challenge\":\"AQ\",\"allowCredentials\":[]}"));

        Assert.IsType<OkObjectResult>(await _controller.AssertionOptionsPost(new LoginOptionsRequest("known-user"), default));
        _passkeyServiceMock.Verify(service => service.GetAssertionOptionsAsync(null, default), Times.Once);
        _userManagerMock.Verify(manager => manager.FindByNameAsync(It.IsAny<string>()), Times.Never);
        _userManagerMock.Verify(manager => manager.FindByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MakeAssertion_PartialCookieWithDifferentCredentialUser_ShouldDeny()
    {
        ArrangeTwoFactorPartialAuthentication(CreateEligibleUser("first-factor-user"));
        ArrangeVerifiedAssertion(CreateEligibleUser("other-user"));
        Assert.IsType<BadRequestObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_PartialCookieWithMatchingCredentialUser_ShouldCompleteLogin()
    {
        var user = CreateEligibleUser("first-factor-user");
        ArrangeTwoFactorPartialAuthentication(user);
        ArrangeVerifiedAssertion(user);
        Assert.IsType<OkObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
        _signInManagerMock.Verify(manager => manager.SignInWithClaimsAsync(user, false, It.IsAny<IEnumerable<Claim>>()), Times.Once);
    }

    [Fact]
    public async Task MakeAssertion_ProtectedOptionsAfterCookieExpires_ShouldDenyBeforeVerification()
    {
        var user = CreateEligibleUser("expired-first-factor");
        ArrangeVerifiedAssertion(user);
        _session.SetString("fido2.assertionUserId", user.Id.ToString());
        Assert.IsType<BadRequestObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
        _passkeyServiceMock.Verify(service => service.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoSuccessfulSignIn();
    }

    [Theory]
    [InlineData("mfa.errors.passkeyNotRegistered")]
    [InlineData("Invalid assertion response")]
    public async Task MakeAssertion_UnverifiedResponse_ShouldReturnUniformFailureAndConsumeOptions(string internalError)
    {
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"AQ\"}");
        _passkeyServiceMock.Setup(service => service.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync((false, (ApplicationUser?)null, false, internalError));
        var result = Assert.IsType<BadRequestObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
        Assert.Equal("Authentication failed", result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
        Assert.DoesNotContain("fido2.assertionOptions", _session.Keys);
        Assert.IsType<BadRequestObjectResult>(await _controller.MakeAssertion(EmptyClientResponse(), default));
        _passkeyServiceMock.Verify(service => service.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
        VerifyNoSuccessfulSignIn();
    }

    [Fact]
    public async Task MakeAssertion_AuthenticatedStepUpWithDifferentCredentialUser_IsRejected()
    {
        var stepUpUser = CreateEligibleUser("step-up-user");
        var credentialUser = CreateEligibleUser("different-user");
        ArrangeApplicationCookieUser(stepUpUser);
        ArrangeVerifiedAssertion(credentialUser);

        var result = await _controller.MakeAssertion(
            EmptyClientResponse(),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = (string?)badRequest.Value!.GetType().GetProperty("error")?.GetValue(badRequest.Value);
        Assert.Equal("Authentication failed", error);
        VerifyNoSuccessfulSignIn();
    }

    private void ArrangeVerifiedAssertion(ApplicationUser user, bool userVerified = true)
    {
        _session.SetString("fido2.assertionOptions", "{\"challenge\":\"123\"}");
        _passkeyServiceMock
            .Setup(service => service.VerifyAssertionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, userVerified, (string?)null));
    }

    private void ArrangeAuthenticatedUser(ApplicationUser user)
    {
        _userManagerMock
            .Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);
    }

    private void ArrangeTwoFactorPartialAuthentication(ApplicationUser user)
    {
        _userManagerMock.Setup(manager => manager.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!),
             MfaEnrollmentSession.BeginInitial(_session, user.Id, securityStamp: user.SecurityStamp)],
            IdentityConstants.TwoFactorUserIdScheme));
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(
                new AuthenticationTicket(principal, IdentityConstants.TwoFactorUserIdScheme)));
        _controller.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
    }

    private static ApplicationUser CreateEligibleUser(string userName)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Status = PersonStatus.Active
        };
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            SecurityStamp = "test-stamp",
            PersonId = person.Id,
            Person = person,
            IsActive = true
        };
    }

    private void ArrangeApplicationCookieUser(ApplicationUser user)
    {
        _userManagerMock.Object.Options = new IdentityOptions();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)],
            IdentityConstants.ApplicationScheme));
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(
                new AuthenticationTicket(
                    principal,
                    IdentityConstants.ApplicationScheme)));
        _userManagerMock
            .Setup(manager => manager.GetUserAsync(principal))
            .ReturnsAsync(user);

        _controller.HttpContext.User = principal;
        _controller.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .AddSingleton(_lifecycleEligibilityMock.Object)
            .AddSingleton(_migrationIssuanceGuardMock.Object)
            .AddSingleton(_userManagerMock.Object)
            .AddSingleton<IApplicationDbContext>(_dbContext)
            .AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider();
    }

    private static System.Text.Json.JsonElement EmptyClientResponse() =>
        System.Text.Json.JsonDocument.Parse("{}").RootElement;

    private void VerifyNoSuccessfulSignIn()
    {
        _signInManagerMock.Verify(
            manager => manager.SignInWithClaimsAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>>()),
            Times.Never);
        _userManagementServiceMock.Verify(
            service => service.UpdateLastLoginAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.DoesNotContain(
            AuthenticationMethodSession.SessionKey,
            _session.Keys);
    }
}
