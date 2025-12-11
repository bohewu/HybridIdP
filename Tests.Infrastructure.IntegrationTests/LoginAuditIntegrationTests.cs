using System;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Options;
using Core.Domain.Constants;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

/// <summary>
/// Integration tests for LoginAttempt audit logging.
/// Verifies that LoginAttemptEvent is correctly handled by AuditService.
/// </summary>
public class LoginAuditIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _auditService;
    private readonly Mock<IDomainEventPublisher> _eventPublisher = new();
    private readonly Mock<ISettingsService> _settingsService = new();

    public LoginAuditIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        
        _settingsService.Setup(s => s.GetValueAsync<int>(SettingKeys.Audit.RetentionDays, default))
            .ReturnsAsync(30);
        
        // Use Partial masking to verify PII protection is applied
        var auditOptions = Options.Create(new AuditOptions { PiiMaskingLevel = PiiMaskingLevel.Partial });
        _auditService = new AuditService(_db, _db, _eventPublisher.Object, _settingsService.Object, auditOptions);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    [Fact]
    public async Task HandleAsync_LoginAttemptEvent_Success_CreatesAuditEvent()
    {
        // Arrange
        var loginEvent = new LoginAttemptEvent(
            userId: "user-123",
            userName: "testuser@example.com",
            isSuccessful: true,
            failureReason: null,
            ipAddress: "192.168.1.100",
            userAgent: "Mozilla/5.0 Test Browser"
        );

        // Act
        await _auditService.HandleAsync(loginEvent);

        // Assert
        var auditEvent = await _db.AuditEvents.FirstOrDefaultAsync();
        Assert.NotNull(auditEvent);
        Assert.Equal("LoginAttempt", auditEvent.EventType);
        Assert.Equal("user-123", auditEvent.UserId);
        Assert.Equal("192.168.1.100", auditEvent.IPAddress);
        Assert.Equal("Mozilla/5.0 Test Browser", auditEvent.UserAgent);
        
        // Verify PII masking is applied - original value should NOT be present
        Assert.DoesNotContain("testuser@example.com", auditEvent.Details!);
        // But masked value should be present (partial mask shows first few chars + ***)
        Assert.Contains("tes***", auditEvent.Details!);
        Assert.Contains("successful", auditEvent.Details!); // successful login
    }

    [Fact]
    public async Task HandleAsync_LoginAttemptEvent_Failure_CreatesAuditEvent()
    {
        // Arrange
        var loginEvent = new LoginAttemptEvent(
            userId: string.Empty,
            userName: "unknownuser",
            isSuccessful: false,
            failureReason: "Invalid credentials",
            ipAddress: "10.0.0.50",
            userAgent: "curl/7.68.0"
        );

        // Act
        await _auditService.HandleAsync(loginEvent);

        // Assert
        var auditEvent = await _db.AuditEvents.FirstOrDefaultAsync();
        Assert.NotNull(auditEvent);
        Assert.Equal("LoginAttempt", auditEvent.EventType);
        
        // Verify PII masking is applied
        Assert.DoesNotContain("unknownuser", auditEvent.Details!);
        Assert.Contains("unk***", auditEvent.Details!); // Masked username
        Assert.Contains("failed", auditEvent.Details!); // failed login
        Assert.Contains("Invalid credentials", auditEvent.Details!);
    }

    [Fact]
    public async Task HandleAsync_LoginAttemptEvent_LockedOut_CreatesAuditEvent()
    {
        // Arrange
        var loginEvent = new LoginAttemptEvent(
            userId: string.Empty,
            userName: "lockeduser",
            isSuccessful: false,
            failureReason: "Account locked out",
            ipAddress: "192.168.1.1",
            userAgent: "Test Agent"
        );

        // Act
        await _auditService.HandleAsync(loginEvent);

        // Assert
        var auditEvent = await _db.AuditEvents.FirstOrDefaultAsync();
        Assert.NotNull(auditEvent);
        
        // Verify PII masking is applied
        Assert.DoesNotContain("lockeduser", auditEvent.Details!);
        Assert.Contains("loc***", auditEvent.Details!); // Masked username
        Assert.Contains("Account locked out", auditEvent.Details!);
    }
}
