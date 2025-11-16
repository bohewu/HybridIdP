using Core.Application;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class LoginHistoryServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly LoginHistoryService _loginHistoryService;

    public LoginHistoryServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new ApplicationDbContext(options);
        
        _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
        _securityPolicyServiceMock.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy
            {
                AbnormalLoginHistoryCount = 10,
                BlockAbnormalLogin = false
            });
        
        _loginHistoryService = new LoginHistoryService(_dbContext, _securityPolicyServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region RecordLoginAsync Tests

    [Fact]
    public async Task RecordLoginAsync_ShouldAddLoginToDatabase()
    {
        // Arrange
        var login = new LoginHistory
        {
            UserId = Guid.NewGuid(),
            LoginTime = DateTime.UtcNow,
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            IsSuccessful = true,
            RiskScore = 0,
            IsFlaggedAbnormal = false
        };

        // Act
        await _loginHistoryService.RecordLoginAsync(login);

        // Assert
        var savedLogin = await _dbContext.LoginHistories.FirstOrDefaultAsync(l => l.UserId == login.UserId);
        Assert.NotNull(savedLogin);
        Assert.Equal(login.IpAddress, savedLogin.IpAddress);
        Assert.Equal(login.UserAgent, savedLogin.UserAgent);
        Assert.True(savedLogin.IsSuccessful);
    }

    #endregion

    #region GetLoginHistoryAsync Tests

    [Fact]
    public async Task GetLoginHistoryAsync_ShouldReturnRecentLogins()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var logins = new List<LoginHistory>
        {
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-1), IpAddress = "192.168.1.1", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false },
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-2), IpAddress = "192.168.1.2", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false },
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-3), IpAddress = "192.168.1.3", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false }
        };

        await _dbContext.LoginHistories.AddRangeAsync(logins);
        await _dbContext.SaveChangesAsync();

        // Act
        var history = await _loginHistoryService.GetLoginHistoryAsync(userId, 2);

        // Assert
        Assert.Equal(2, history.Count());
        Assert.Equal("192.168.1.1", history.First().IpAddress);
        Assert.Equal("192.168.1.2", history.Last().IpAddress);
    }

    #endregion

    #region DetectAbnormalLoginAsync Tests

    [Fact]
    public async Task DetectAbnormalLoginAsync_ShouldReturnFalse_ForNormalLogin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingLogins = new List<LoginHistory>
        {
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-1), IpAddress = "192.168.1.1", UserAgent = "Mozilla/5.0", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false },
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-2), IpAddress = "192.168.1.1", UserAgent = "Mozilla/5.0", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false }
        };

        await _dbContext.LoginHistories.AddRangeAsync(existingLogins);
        await _dbContext.SaveChangesAsync();

        var currentLogin = new LoginHistory
        {
            UserId = userId,
            LoginTime = DateTime.UtcNow,
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            IsSuccessful = true,
            RiskScore = 0,
            IsFlaggedAbnormal = false
        };

        // Act
        var isAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(currentLogin);

        // Assert
        Assert.False(isAbnormal);
    }

    [Fact]
    public async Task DetectAbnormalLoginAsync_ShouldReturnTrue_ForNewIpAddress()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingLogins = new List<LoginHistory>
        {
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-1), IpAddress = "192.168.1.1", UserAgent = "Mozilla/5.0", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false },
            new() { UserId = userId, LoginTime = DateTime.UtcNow.AddHours(-2), IpAddress = "192.168.1.1", UserAgent = "Mozilla/5.0", IsSuccessful = true, RiskScore = 0, IsFlaggedAbnormal = false }
        };

        await _dbContext.LoginHistories.AddRangeAsync(existingLogins);
        await _dbContext.SaveChangesAsync();

        var currentLogin = new LoginHistory
        {
            UserId = userId,
            LoginTime = DateTime.UtcNow,
            IpAddress = "10.0.0.1", // New IP
            UserAgent = "Mozilla/5.0",
            IsSuccessful = true,
            RiskScore = 0,
            IsFlaggedAbnormal = false
        };

        // Act
        var isAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(currentLogin);

        // Assert
        Assert.True(isAbnormal);
    }

    [Fact]
    public async Task DetectAbnormalLoginAsync_ShouldReturnFalse_ForFirstLogin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentLogin = new LoginHistory
        {
            UserId = userId,
            LoginTime = DateTime.UtcNow,
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            IsSuccessful = true,
            RiskScore = 0,
            IsFlaggedAbnormal = false
        };

        // Act
        var isAbnormal = await _loginHistoryService.DetectAbnormalLoginAsync(currentLogin);

        // Assert
        Assert.False(isAbnormal);
    }

    #endregion
}