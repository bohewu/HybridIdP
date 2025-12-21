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

namespace Tests.Web.IdP.UnitTests.Controllers;

public class PasskeyControllerTests
{
    private readonly Mock<IPasskeyService> _passkeyServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<ILogger<PasskeyController>> _loggerMock;
    private readonly Mock<ISession> _sessionMock;
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

        _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
        
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _auditServiceMock = new Mock<IAuditService>();
        _loggerMock = new Mock<ILogger<PasskeyController>>();
        
        // Mock Session
        _sessionMock = new Mock<ISession>();
        var httpContext = new DefaultHttpContext();
        httpContext.Session = _sessionMock.Object;

        _controller = new PasskeyController(
            _passkeyServiceMock.Object,
            _signInManagerMock.Object,
            _userManagerMock.Object,
            _securityPolicyServiceMock.Object,
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
        var person = new Person { Status = PersonStatus.Suspended };
        var user = new ApplicationUser { Person = person, IsActive = true };
        
        // Mock session data
        var sessionValue = System.Text.Encoding.UTF8.GetBytes("{\"challenge\":\"123\"}");
        _sessionMock.Setup(s => s.TryGetValue("fido2.assertionOptions", out sessionValue)).Returns(true);

        _passkeyServiceMock.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, (string?)null));

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
        
        // Verify SignIn was NOT called
        _signInManagerMock.Verify(x => x.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MakeAssertion_DeactivatedUser_ReturnsBadRequest()
    {
        // Arrange
        var person = new Person { Status = PersonStatus.Active };
        var user = new ApplicationUser { Person = person, IsActive = false }; // Deactivated
        
        // Mock session data
        var sessionValue = System.Text.Encoding.UTF8.GetBytes("{\"challenge\":\"123\"}");
        _sessionMock.Setup(s => s.TryGetValue("fido2.assertionOptions", out sessionValue)).Returns(true);

        _passkeyServiceMock.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, (string?)null));

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

        _signInManagerMock.Verify(x => x.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MakeAssertion_ActivePersonAndUser_ReturnsOkAndSignsIn()
    {
        // Arrange
        var person = new Person { Status = PersonStatus.Active };
        var user = new ApplicationUser { UserName="testuser", Person = person, IsActive = true };
        
        var sessionValue = System.Text.Encoding.UTF8.GetBytes("{\"challenge\":\"123\"}");
        _sessionMock.Setup(s => s.TryGetValue("fido2.assertionOptions", out sessionValue)).Returns(true);

        _passkeyServiceMock.Setup(x => x.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, (string?)null));

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
    }
}
