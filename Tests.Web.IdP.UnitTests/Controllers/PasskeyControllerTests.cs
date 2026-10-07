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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Controllers.Account;
using Xunit;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class PasskeyControllerTests
{
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
        _passkeyServiceMock.Setup(s => s.DeletePasskeyAsync(user.Id, credentialId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

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
        _controller.HttpContext.RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
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
                    new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }.Concat(claims), IdentityConstants.ApplicationScheme));
            }).Returns(Task.CompletedTask);
        Assert.IsType<OkObjectResult>(await _controller.MakeCredential(EmptyClientResponse(), default));
        Assert.True(MfaEnrollmentSession.HasMfa(cookie));
        Assert.IsType<OkObjectResult>(await _controller.ListPasskeys(default));
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
            .BuildServiceProvider();

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
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), MfaEnrollmentSession.BeginInitial(_session, user.Id)],
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
            .BuildServiceProvider();
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
            .BuildServiceProvider();
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
