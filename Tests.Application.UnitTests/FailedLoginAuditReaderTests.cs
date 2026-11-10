using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Tests.Application.UnitTests;

public class FailedLoginAuditReaderTests
{
    [Theory]
    [InlineData("LoginAttempt", "Login attempt for user 'masked': failed (Invalid credentials)", true)]
    [InlineData("LoginAttempt", "Login attempt for user 'masked': failed ()", true)]
    [InlineData("LoginAttempt", "Login attempt for user 'masked': successful", false)]
    [InlineData("LoginAttempt", "Login attempt for user 'failed': successful", false)]
    [InlineData("LoginAttempt", "Login attempt for user 'name': failed (in username)': successful", false)]
    [InlineData("LoginAttempt", "Login attempt for user 'O'Name': failed (Account locked)", true)]
    [InlineData("LoginAttempt", "failed (Invalid credentials)", false)]
    [InlineData("LoginAttempt", "Login attempt for user 'masked': failed", false)]
    [InlineData("LoginAttempt", "Login attempt for user 'masked': failed (unfinished", false)]
    [InlineData("LoginAttempt", "{\"Success\":false}", false)]
    [InlineData("LoginAttempt", "{\"details\":\"Login attempt for user 'masked': failed (Invalid credentials)\"}", false)]
    [InlineData("LoginAttempt", "{\"details\":{\"Success\":false},\"impersonation\":{}}", false)]
    [InlineData("LoginAttempt", "{\"details\":\"Login attempt for user 'masked': failed (Invalid credentials)\",\"impersonation\":{\"actorUserId\":\"11111111-1111-1111-1111-111111111111\",\"subjectUserId\":\"22222222-2222-2222-2222-222222222222\"}}", true)]
    [InlineData("LoginAttempt", "{\"details\":\"Login attempt for user 'failed': successful\",\"impersonation\":{}}", false)]
    [InlineData("UserLogin", "{\"Success\":false,\"FailureReason\":\"Invalid password\"}", true)]
    [InlineData("UserLogin", "{\"Success\":false,\"FailureReason\":\"Additional authentication required\",\"impersonation\":{}}", true)]
    [InlineData("UserLogin", "{\"Success\":true,\"FailureReason\":\"failed\"}", false)]
    [InlineData("UserLogin", "{\"Success\":\"false\"}", false)]
    [InlineData("UserLogin", "{\"Success\":0}", false)]
    [InlineData("UserLogin", "{\"success\":false}", false)]
    [InlineData("UserLogin", "{\"nested\":{\"Success\":false}}", false)]
    [InlineData("UserLogin", "[{\"Success\":false}]", false)]
    [InlineData("UserLogin", "null", false)]
    [InlineData("UserLogin", "{}", false)]
    [InlineData("UserLogin", "{broken", false)]
    [InlineData("UserLogin", "", false)]
    [InlineData("UserLogin", null, false)]
    [InlineData("Unrelated", "{\"Success\":false}", false)]
    [InlineData("Logout", "Login attempt for user 'masked': failed (Invalid credentials)", false)]
    public void IsFailure_ShouldRecognizeOnlyWriterOutcomes(string eventType, string? details, bool expected)
    {
        Assert.Equal(expected, FailedLoginAuditReader.IsFailure(eventType, details));
    }

    [Fact]
    public async Task ReadAsync_ShouldUseHalfOpenUtcWindowAndPreserveUnknownSubjectsAndDistinctRecords()
    {
        await using var db = CreateContext();
        var start = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var timestamp = start.AddHours(1);
        db.AuditEvents.AddRange(
            Event("LoginAttempt", start.AddTicks(-1), null),
            Event("LoginAttempt", start, null),
            Event("LoginAttempt", timestamp, ""),
            Event("UserLogin", timestamp, ""),
            Event("LoginAttempt", end.AddTicks(-1), "known"),
            Event("LoginAttempt", end, null),
            Event("LoginAttempt", end.AddHours(1), null),
            new AuditEvent { EventType = "Unrelated", Timestamp = timestamp, Details = "{\"Success\":false}" },
            new AuditEvent { EventType = "UserLogin", Timestamp = timestamp, Details = "{broken" },
            new AuditEvent { EventType = "LoginAttempt", Timestamp = timestamp, Details = "Login attempt for user 'failed': successful" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var failures = new List<FailedLoginAuditResult>();
        await foreach (var failure in FailedLoginAuditReader.ReadAsync(db, start, end))
            failures.Add(failure);

        Assert.Equal(4, failures.Count);
        Assert.Contains(failures, e => e.Timestamp == start && e.UserId is null);
        Assert.Equal(2, failures.Count(e => e.Timestamp == timestamp && e.UserId == ""));
        Assert.Equal(4, failures.Select(e => e.Id).Distinct().Count());
        Assert.All(failures, e =>
        {
            Assert.Equal("192.0.2.1", e.IpAddress);
            Assert.Equal("synthetic-agent", e.UserAgent);
        });
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ReadAsync_ShouldHonorCancellation()
    {
        await using var db = CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var failure in FailedLoginAuditReader.ReadAsync(
                db, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, cancellation.Token)) { }
        });
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AuditEvent Event(string eventType, DateTime timestamp, string? userId) => new()
    {
        EventType = eventType,
        Timestamp = timestamp,
        UserId = userId,
        Details = eventType == "UserLogin" ? "{\"Success\":false,\"FailureReason\":\"Invalid password\"}"
            : "Login attempt for user 'masked': failed (Invalid credentials)",
        IPAddress = "192.0.2.1",
        UserAgent = "synthetic-agent"
    };
}
