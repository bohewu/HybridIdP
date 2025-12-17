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

namespace Tests.Web.IdP.UnitTests.Controllers;

public class PasskeyControllerTests
{
    private readonly Mock<IPasskeyService> _passkeyServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly ApplicationDbContext _dbContext;
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
            .ReturnsAsync(new WebAuthnResult { Success = true, User = user });

        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _controller.MakeAssertion(clientResponse, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var val = badRequest.Value; 
        // Use reflection or dynamic to check error property? Or just check type.
        // Assuming implementation returns new { success = false, error = "Account not active" }
        // Using dynamic for simplicity in test
        dynamic data = val!;
        Assert.False((bool)data.success);
        Assert.Equal("Account not active", (string)data.error);
        
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
            .ReturnsAsync(new WebAuthnResult { Success = true, User = user });

        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _controller.MakeAssertion(clientResponse, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        dynamic data = badRequest.Value!;
        Assert.False((bool)data.success);
        Assert.Equal("User account deactivated", (string)data.error);

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
            .ReturnsAsync(new WebAuthnResult { Success = true, User = user });

        var clientResponse = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _controller.MakeAssertion(clientResponse, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic data = okResult.Value!;
        Assert.True((bool)data.success);
        Assert.Equal("testuser", (string)data.username);

        // Verify SignIn WAS called
        _signInManagerMock.Verify(x => x.SignInAsync(user, false, null), Times.Once);
    }
}
