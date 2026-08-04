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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Controllers.Account;
using Xunit;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class PasskeyControllerTests
{
    private readonly Mock<IPasskeyService> _passkeyServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly Mock<IUserManagementService> _userManagementServiceMock;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<ILogger<PasskeyController>> _loggerMock;
    private readonly MemorySession _session;
    private readonly PasskeyController _controller;

    public PasskeyControllerTests()
    {
        _passkeyServiceMock = new Mock<IPasskeyService>();
        
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
        _loggerMock = new Mock<ILogger<PasskeyController>>();
        
        _session = new MemorySession();
        var httpContext = new DefaultHttpContext();
        httpContext.Session = _session;

        _controller = new PasskeyController(
            _passkeyServiceMock.Object,
            _signInManagerMock.Object,
            _userManagerMock.Object,
            _securityPolicyServiceMock.Object,
            _userManagementServiceMock.Object,
            _dbContext,
            _auditServiceMock.Object,
            _loggerMock.Object
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
            PersonId = person.Id,
            Person = person,
            IsActive = true
        };
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
