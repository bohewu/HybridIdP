using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Web.IdP.Controllers.Admin;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class SecurityPolicyControllerTests
{
    private readonly Mock<ISecurityPolicyService> _mockService;
    private readonly SecurityPolicyController _controller;

    public SecurityPolicyControllerTests()
    {
        _mockService = new Mock<ISecurityPolicyService>();
        _controller = new SecurityPolicyController(_mockService.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "test_user_id"),
            new Claim(ClaimTypes.Name, "TestUser")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetCurrentPolicy_ReturnsOkWithPolicy()
    {
        // Arrange
        var policy = new SecurityPolicy { MinPasswordLength = 10 };
        _mockService.Setup(s => s.GetCurrentPolicyAsync()).ReturnsAsync(policy);

        // Act
        var result = await _controller.GetPolicy();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPolicy = Assert.IsType<SecurityPolicy>(okResult.Value);
        Assert.Equal(10, returnedPolicy.MinPasswordLength);
    }

    [Fact]
    public async Task UpdatePolicy_ValidDto_CallsServiceAndReturnsOk()
    {
        // Arrange
        var dto = new SecurityPolicyDto { MinPasswordLength = 12 };
        var updatedPolicy = new SecurityPolicy { MinPasswordLength = 12 };

        _mockService.Setup(s => s.UpdatePolicyAsync(dto, "TestUser"))
            .Returns(Task.CompletedTask)
            .Verifiable();
        
        _mockService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(updatedPolicy);

        // Act
        var result = await _controller.UpdatePolicy(dto);

        // Assert
        _mockService.Verify();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPolicy = Assert.IsType<SecurityPolicy>(okResult.Value);
        Assert.Equal(12, returnedPolicy.MinPasswordLength);
    }
}
