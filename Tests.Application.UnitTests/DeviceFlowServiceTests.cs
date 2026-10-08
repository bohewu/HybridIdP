using System.Collections.Immutable;
using System.Security.Claims;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests;

public class DeviceFlowServiceTests
{
    [Theory]
    [InlineData("pwd", false, "amr")]
    [InlineData("hwk", false, "amr")]
    [InlineData("pwd", true, "amr")]
    [InlineData("hwk", true, "amr")]
    [InlineData("hwk", false, ClaimTypes.AuthenticationMethod)]
    [InlineData("pwd", true, ClaimTypes.AuthenticationMethod)]
    public async Task ProcessVerificationAsync_ShouldRequirePerformedMfa_WhenGlobalPolicyActivatesAfterCookieIssued(
        string primaryAmr, bool completedMfa, string claimType)
    {
        var policy = new SecurityPolicy { MfaEnforcementGracePeriodDays = 30 };
        _securityPolicy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(policy);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "stale-device-cookie", IsActive = true };
        var identity = new ClaimsIdentity(
            [new Claim(claimType, primaryAmr)], IdentityConstants.ApplicationScheme);
        if (completedMfa) identity.AddClaim(new Claim(claimType, AuthConstants.Amr.Mfa));
        identity.SetClaim(Claims.AuthenticationTime, DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
        var userPrincipal = new ClaimsPrincipal(identity);
        _mockUserManager.Setup(manager => manager.GetUserAsync(userPrincipal)).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _mockApplicationManager.Setup(manager => manager.GetPropertiesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableDictionary<string, System.Text.Json.JsonElement>.Empty.Add(
                AuthConstants.Properties.RequireMfa, System.Text.Json.JsonSerializer.SerializeToElement(false)));
        _mockScopeManager.Setup(manager => manager.ListResourcesAsync(It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<string>().ToAsyncEnumerable());
        var device = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.ClientId, "test-client")], "device"));
        device.SetScopes(Scopes.OpenId);

        // The cookie predates the policy change; enrollment grace does not establish performed MFA.
        policy.EnforceMandatoryMfaEnrollment = true;
        var result = await _service.ProcessVerificationAsync(userPrincipal,
            AuthenticateResult.Success(new AuthenticationTicket(device, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)));

        _securityPolicy.Verify(service => service.GetCurrentPolicyAsync(), Times.Once);
        if (!completedMfa)
        {
            var denied = Assert.IsType<ForbidResult>(result);
            Assert.Equal(Errors.InvalidGrant, denied.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            Assert.Contains(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, denied.AuthenticationSchemes);
            _mockClaimsEnricher.Verify(enricher => enricher.AddScopeMappedClaimsAsync(
                It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
            return;
        }

        var approved = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!;
        Assert.Contains(AuthConstants.Amr.Mfa, approved.GetClaims(AuthConstants.ClaimTypes.Amr));
        Assert.Contains(primaryAmr, approved.GetClaims(AuthConstants.ClaimTypes.Amr));
        Assert.Equal(user.Id.ToString(), approved.GetClaim(Claims.Subject));
        Assert.Equal<string>(device.GetScopes(), approved.GetScopes());
        Assert.Equal(userPrincipal.GetClaim(Claims.AuthenticationTime), approved.GetClaim(Claims.AuthenticationTime));
    }

    [Theory]
    [InlineData(false, "pwd")]
    [InlineData(false, "hwk")]
    [InlineData(true, "hwk")]
    public async Task ProcessVerificationAsync_ShouldRequirePerformedMfaAndPreserveEvidence(bool completedMfa, string primaryAmr)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "device-user" };
        var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("amr", primaryAmr), new Claim("amr", completedMfa ? "mfa" : "user")], IdentityConstants.ApplicationScheme));
        _mockUserManager.Setup(m => m.GetUserAsync(userPrincipal)).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _mockApplicationManager.Setup(m => m.GetPropertiesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            ImmutableDictionary<string, System.Text.Json.JsonElement>.Empty.Add(AuthConstants.Properties.RequireMfa, System.Text.Json.JsonSerializer.SerializeToElement(true)));
        _mockScopeManager.Setup(m => m.ListResourcesAsync(It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<string>().ToAsyncEnumerable());
        var device = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.ClientId, "test-client")], "device"));
        var result = await _service.ProcessVerificationAsync(userPrincipal,
            AuthenticateResult.Success(new AuthenticationTicket(device, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)));
        if (completedMfa)
            Assert.Contains("mfa", Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!.GetClaims("amr"));
        else Assert.IsType<ForbidResult>(result);
    }

    private readonly Mock<global::Web.IdP.Services.ICurrentUserLifecycleEligibility> _lifecycle = new();

    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly Mock<IOpenIddictAuthorizationManager> _mockAuthorizationManager = new();
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IStringLocalizer<DeviceFlowService>> _mockLocalizer;
    private readonly Mock<ILogger<DeviceFlowService>> _mockLogger;
    private readonly Mock<IClaimsEnrichmentService> _mockClaimsEnricher;
    private readonly Mock<ISecurityPolicyService> _securityPolicy = new();
    private readonly DeviceFlowService _service;

    public DeviceFlowServiceTests()
    {
        _lifecycle.Setup(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockScopeManager = new Mock<IOpenIddictScopeManager>();
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _mockApplicationManager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());
        _mockApplicationManager.Setup(m => m.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.Implicit);
        _mockUserManager = MockUserManager<ApplicationUser>();
        _mockLocalizer = new Mock<IStringLocalizer<DeviceFlowService>>();
        _mockLogger = new Mock<ILogger<DeviceFlowService>>();
        _mockClaimsEnricher = new Mock<IClaimsEnrichmentService>();
        _securityPolicy.Setup(policy => policy.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());

        _mockLocalizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

        _mockClaimsEnricher.Setup(x => x.AddScopeMappedClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .Returns(Task.CompletedTask);
        _mockClaimsEnricher.Setup(x => x.AddPermissionClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>()))
            .Returns(Task.CompletedTask);

        _service = new DeviceFlowService(
            _lifecycle.Object,
            _mockScopeManager.Object,
            _mockApplicationManager.Object,
            _mockUserManager.Object,
            _mockLocalizer.Object,
            _mockLogger.Object,
            _mockClaimsEnricher.Object,
            _securityPolicy.Object,
            _mockAuthorizationManager.Object);
    }

    private static Mock<UserManager<TUser>> MockUserManager<TUser>() where TUser : class
    {
        var store = new Mock<IUserStore<TUser>>();
        return new Mock<UserManager<TUser>>(store.Object, null, null, null, null, null, null, null, null);
    }

    [Theory]
    [InlineData(ConsentTypes.Explicit, false, false)]
    [InlineData(ConsentTypes.Explicit, true, true)]
    [InlineData(ConsentTypes.Systematic, false, false)]
    [InlineData(ConsentTypes.Systematic, true, true)]
    [InlineData(ConsentTypes.Implicit, false, true)]
    [InlineData("unknown", true, false)]
    [InlineData(null, false, false)]
    public async Task ProcessVerificationAsync_ShouldHonorConsentPolicy(string? consentType, bool decision, bool allowed)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));
        _mockUserManager.Setup(manager => manager.GetUserAsync(principal)).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _mockApplicationManager.Setup(manager => manager.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(consentType);
        _mockScopeManager.Setup(manager => manager.ListResourcesAsync(It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<string>().ToAsyncEnumerable());
        var device = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.ClientId, "test-client")], "device"));
        device.SetScopes(Scopes.OpenId, Scopes.Profile);

        var result = await _service.ProcessVerificationAsync(principal,
            AuthenticateResult.Success(new AuthenticationTicket(device, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)),
            consentGranted: decision);

        if (allowed)
            Assert.Equal<string>(device.GetScopes(), Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!.GetScopes());
        else
        {
            var denied = Assert.IsType<ForbidResult>(result);
            Assert.Equal(Errors.AccessDenied, denied.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            _mockClaimsEnricher.Verify(enricher => enricher.AddScopeMappedClaimsAsync(
                It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProcessVerificationAsync_ExternalConsent_ShouldRequireCoveringPermanentAuthorization(bool exists, bool coversScopes)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));
        _mockUserManager.Setup(manager => manager.GetUserAsync(principal)).ReturnsAsync(user);
        _mockUserManager.Setup(manager => manager.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _mockApplicationManager.Setup(manager => manager.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentTypes.External);
        _mockApplicationManager.Setup(manager => manager.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("application-id");
        var authorization = new object();
        _mockAuthorizationManager.Setup(manager => manager.FindAsync(user.Id.ToString(), "application-id", Statuses.Valid,
                AuthorizationTypes.Permanent, It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns((exists ? new[] { authorization } : Array.Empty<object>()).ToAsyncEnumerable());
        _mockAuthorizationManager.Setup(manager => manager.GetScopesAsync(authorization, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coversScopes ? ImmutableArray.Create(Scopes.OpenId, Scopes.Profile) : ImmutableArray.Create(Scopes.OpenId));
        _mockAuthorizationManager.Setup(manager => manager.GetIdAsync(authorization, It.IsAny<CancellationToken>()))
            .ReturnsAsync("approved-authorization");
        _mockScopeManager.Setup(manager => manager.ListResourcesAsync(It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<string>().ToAsyncEnumerable());
        var device = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.ClientId, "test-client")], "device"));
        device.SetScopes(Scopes.OpenId, Scopes.Profile);

        var result = await _service.ProcessVerificationAsync(principal,
            AuthenticateResult.Success(new AuthenticationTicket(device, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)),
            consentGranted: true);

        if (exists && coversScopes)
            Assert.Equal("approved-authorization", Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!.GetAuthorizationId());
        else Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task PrepareVerificationViewModelAsync_ReturnsError_WhenClientNotFound()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(Claims.ClientId, "test-client")
        }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        
        var authResult = AuthenticateResult.Success(new AuthenticationTicket(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

        _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _service.PrepareVerificationViewModelAsync(authResult);

        // Assert
        Assert.Equal(Errors.InvalidClient, result.Error);
        Assert.Equal("InvalidClient", result.ErrorDescription);
    }

    /*
    [Fact]
    public async Task PrepareVerificationViewModelAsync_ReturnsViewModel_WhenValidRequest()
    {
        // ...
        // Assert
        // Assert.Equal("openid profile", result.Scope);
        // ...
    }
    */

    [Fact]
    public async Task ProcessVerificationAsync_ReturnsError_WhenUserNotFound()
    {
        // Arrange
        var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        _mockUserManager.Setup(m => m.GetUserAsync(userPrincipal)).ReturnsAsync((ApplicationUser)null!);

        var authResult = AuthenticateResult.NoResult();

        // Act
        var result = await _service.ProcessVerificationAsync(userPrincipal, authResult);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var vm = Assert.IsType<DeviceVerificationViewModel>(badRequest.Value);
        Assert.Equal(Errors.ServerError, vm.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessVerificationAsync_ReturnsSignInOnlyWhenLifecycleAllows(bool allowed)
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "testuser", Email = "test@test.com" };
        var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        _mockUserManager.Setup(m => m.GetUserAsync(userPrincipal)).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
        _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
        _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        var devicePrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(Claims.ClientId, "test-client"),
            new Claim(Claims.Scope, "openid")
        }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

        var authResult = AuthenticateResult.Success(new AuthenticationTicket(devicePrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));

        _mockScopeManager.Setup(m => m.ListResourcesAsync(It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<string>().ToAsyncEnumerable());

        _lifecycle.Setup(policy => policy.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
        var result = await _service.ProcessVerificationAsync(userPrincipal, authResult);
        _lifecycle.Verify(policy => policy.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        if (!allowed)
        {
            var denied = Assert.IsType<ForbidResult>(result);
            Assert.Equal(Errors.InvalidGrant, denied.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            return;
        }
        var signInResult = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signInResult.AuthenticationScheme);
        Assert.NotNull(signInResult.Principal);
        Assert.True(signInResult.Principal.HasClaim(c => c.Type == Claims.Subject && c.Value == user.Id.ToString()));
    }
}
