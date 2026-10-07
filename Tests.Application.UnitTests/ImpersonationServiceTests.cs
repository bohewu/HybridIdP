#nullable disable
using System.Security.Claims;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Moq;
using Web.IdP.Services;
using Xunit;

namespace Tests.Application.UnitTests;

public class ImpersonationServiceTests
{
    private readonly Mock<global::Web.IdP.Services.ICurrentUserLifecycleEligibility> _lifecycle = new();

    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IUserClaimsPrincipalFactory<ApplicationUser>> _claimsFactoryMock;
    private readonly ImpersonationService _sut; // System Under Test

    public ImpersonationServiceTests()
    {
        _lifecycle.Setup(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
        _claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _sut = new ImpersonationService(_lifecycle.Object,
_userManagerMock.Object, _claimsFactoryMock.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Impersonation_ShouldNotBuildPrincipal_WhenReceivingAccountLifecycleDenies(bool restore)
    {
        var admin = new ApplicationUser { Id = Guid.NewGuid() };
        var target = new ApplicationUser { Id = Guid.NewGuid() };
        _userManagerMock.Setup(manager => manager.FindByIdAsync(admin.Id.ToString())).ReturnsAsync(admin);
        _userManagerMock.Setup(manager => manager.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        var receivingId = restore ? admin.Id : target.Id;
        _lifecycle.Setup(policy => policy.IsEligibleAsync(receivingId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var identity = new ClaimsIdentity("test") { Actor = new ClaimsIdentity([new Claim("sub", admin.Id.ToString())]) };
        var result = restore ? await _sut.RevertImpersonationAsync(new ClaimsPrincipal(identity))
            : await _sut.StartImpersonationAsync(admin.Id, target.Id);
        Assert.False(result.Success);
        _claimsFactoryMock.Verify(factory => factory.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _lifecycle.Verify(policy => policy.IsEligibleAsync(receivingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartImpersonationAsync_ShouldFail_WhenSelfImpersonation()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _sut.StartImpersonationAsync(userId, userId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Cannot impersonate yourself", result.Error);
    }

    [Fact]
    public async Task StartImpersonationAsync_ShouldFail_WhenTargetIsAdmin()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var adminUser = new ApplicationUser { Id = adminId, UserName = "admin" };
        var targetUser = new ApplicationUser { Id = targetId, UserName = "target" };

        _userManagerMock.Setup(x => x.FindByIdAsync(adminId.ToString())).ReturnsAsync(adminUser);
        _userManagerMock.Setup(x => x.FindByIdAsync(targetId.ToString())).ReturnsAsync(targetUser);
        _userManagerMock.Setup(x => x.IsInRoleAsync(targetUser, AuthConstants.Roles.Admin)).ReturnsAsync(true);

        // Act
        var result = await _sut.StartImpersonationAsync(adminId, targetId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Cannot impersonate another administrator", result.Error);
    }

    [Fact]
    public async Task StartImpersonationAsync_ShouldSucceed_WhenValid()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var adminUser = new ApplicationUser { Id = adminId, UserName = "admin" };
        var targetUser = new ApplicationUser { Id = targetId, UserName = "target" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        _userManagerMock.Setup(x => x.FindByIdAsync(adminId.ToString())).ReturnsAsync(adminUser);
        _userManagerMock.Setup(x => x.FindByIdAsync(targetId.ToString())).ReturnsAsync(targetUser);
        _userManagerMock.Setup(x => x.IsInRoleAsync(targetUser, AuthConstants.Roles.Admin)).ReturnsAsync(false);
        _claimsFactoryMock.Setup(x => x.CreateAsync(targetUser)).ReturnsAsync(principal);

        // Act
        var result = await _sut.StartImpersonationAsync(adminId, targetId);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Principal);
        Assert.NotNull(((ClaimsIdentity)result.Principal!.Identity!).Actor);
        Assert.Equal(adminId.ToString(), ((ClaimsIdentity)result.Principal.Identity).Actor.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task RevertImpersonationAsync_ShouldFail_WhenNotImpersonating()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = await _sut.RevertImpersonationAsync(principal);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Not currently impersonating", result.Error);
    }

    [Fact]
    public async Task RevertImpersonationAsync_ShouldSucceed_WhenImpersonating()
    {
        // Arrange
        var originalAdminId = Guid.NewGuid();
        var originalUser = new ApplicationUser { Id = originalAdminId, UserName = "admin" };
        
        var identity = new ClaimsIdentity("cookie");
        var actorIdentity = new ClaimsIdentity();
        actorIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, originalAdminId.ToString()));
        identity.Actor = actorIdentity;
        
        var principal = new ClaimsPrincipal(identity);
        var originalPrincipal = new ClaimsPrincipal(new ClaimsIdentity());

        _userManagerMock.Setup(x => x.FindByIdAsync(originalAdminId.ToString())).ReturnsAsync(originalUser);
        _claimsFactoryMock.Setup(x => x.CreateAsync(originalUser)).ReturnsAsync(originalPrincipal);

        // Act
        var result = await _sut.RevertImpersonationAsync(principal);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Principal);
    }

    [Fact]
    public async Task RevertImpersonationAsync_ShouldRestoreOriginalActor_WhenOnlyPreservedCookieClaimRemains()
    {
        var actorId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var originalUser = new ApplicationUser { Id = actorId };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, subjectId.ToString()),
            new Claim(AuthConstants.Claims.ImpersonatorId, actorId.ToString())], "cookie"));
        var restored = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "cookie"));
        _userManagerMock.Setup(manager => manager.FindByIdAsync(actorId.ToString())).ReturnsAsync(originalUser);
        _claimsFactoryMock.Setup(factory => factory.CreateAsync(originalUser)).ReturnsAsync(restored);

        var result = await _sut.RevertImpersonationAsync(principal);

        Assert.True(result.Success);
        Assert.Same(restored, result.Principal);
        Assert.Null(((ClaimsIdentity)restored.Identity!).Actor);
        Assert.Null(restored.FindFirst(AuthConstants.Claims.ImpersonatorId));
        _userManagerMock.Verify(manager => manager.FindByIdAsync(subjectId.ToString()), Times.Never);
    }
}
