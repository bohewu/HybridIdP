using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Hubs;
using Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace Tests.Application.UnitTests;

public sealed class ActivityStatisticsTests : IDisposable
{
    private readonly DateTime _now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly ApplicationDbContext _db;
    private readonly DashboardService _dashboard;
    private readonly MonitoringService _monitoring;

    public ActivityStatisticsTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FakeTimeProvider(new DateTimeOffset(_now));
        _dashboard = new DashboardService(_db, Mock.Of<IOpenIddictApplicationManager>(),
            Mock.Of<IOpenIddictScopeManager>(), Mock.Of<IOpenIddictAuthorizationManager>(), clock);
        _monitoring = new MonitoringService(_db, null!, Mock.Of<IHttpClientFactory>(),
            Mock.Of<IHubContext<MonitoringHub>>(), Options.Create(new ObservabilityOptions()), clock);
    }

    [Theory]
    [InlineData("access", 1)]
    [InlineData("refresh", 1)]
    [InlineData("access-token-hint", 0)]
    [InlineData("refresh-token-hint", 0)]
    [InlineData("multiple-tokens", 1)]
    [InlineData("expired-access-valid-refresh", 1)]
    [InlineData("valid-access-expired-refresh", 1)]
    [InlineData("revoked-authorization", 0)]
    [InlineData("rejected-authorization", 0)]
    [InlineData("no-token", 0)]
    [InlineData("expired-token", 0)]
    [InlineData("expiry-at-now", 0)]
    [InlineData("revoked-token", 0)]
    [InlineData("redeemed-refresh", 0)]
    [InlineData("code-only", 0)]
    [InlineData("id-token-only", 0)]
    [InlineData("device-code-only", 0)]
    [InlineData("null-token-expiry", 0)]
    [InlineData("legacy-no-ledger", 1)]
    [InlineData("ledger-null-bounds", 1)]
    [InlineData("ledger-future-bounds", 1)]
    [InlineData("ledger-revoked", 0)]
    [InlineData("ledger-absolute-expired", 0)]
    [InlineData("ledger-sliding-expired", 0)]
    [InlineData("ledger-absolute-at-now", 0)]
    [InlineData("ledger-sliding-at-now", 0)]
    [InlineData("ledger-wrong-user", 0)]
    [InlineData("ledger-wrong-client", 0)]
    [InlineData("token-wrong-subject", 0)]
    [InlineData("token-wrong-application", 0)]
    [InlineData("token-no-application", 0)]
    [InlineData("authorization-no-application", 0)]
    [InlineData("missing-user", 0)]
    [InlineData("no-subject", 0)]
    public async Task GetActivityStatsAsync_ShouldApplySameEffectiveSessionPredicate_InBothServices(
        string scenario, int expected)
    {
        var (authorization, token, ledger) = AddSession();
        switch (scenario)
        {
            // Pin the stored values independently of the query constants.
            case "access": token.Type = "urn:ietf:params:oauth:token-type:access_token"; break;
            case "refresh": token.Type = "urn:ietf:params:oauth:token-type:refresh_token"; break;
            case "access-token-hint": token.Type = OpenIddictConstants.TokenTypeHints.AccessToken; break;
            case "refresh-token-hint": token.Type = OpenIddictConstants.TokenTypeHints.RefreshToken; break;
            case "multiple-tokens": AddToken(authorization); AddToken(authorization, true); break;
            case "expired-access-valid-refresh":
                token.ExpirationDate = _now.AddMinutes(-1); AddToken(authorization, true); break;
            case "valid-access-expired-refresh":
                AddToken(authorization, true).ExpirationDate = _now.AddMinutes(-1); break;
            case "revoked-authorization": authorization.Status = OpenIddictConstants.Statuses.Revoked; break;
            case "rejected-authorization": authorization.Status = OpenIddictConstants.Statuses.Rejected; break;
            case "no-token": _db.Remove(token); break;
            case "expired-token": token.ExpirationDate = _now.AddTicks(-1); break;
            case "expiry-at-now": token.ExpirationDate = _now; break;
            case "revoked-token": token.Status = OpenIddictConstants.Statuses.Revoked; break;
            case "redeemed-refresh":
                token.Type = OpenIddictConstants.TokenTypeIdentifiers.RefreshToken;
                token.Status = OpenIddictConstants.Statuses.Redeemed; break;
            case "code-only": token.Type = OpenIddictConstants.TokenTypeIdentifiers.Private.AuthorizationCode; break;
            case "id-token-only": token.Type = OpenIddictConstants.TokenTypeIdentifiers.IdentityToken; break;
            case "device-code-only": token.Type = OpenIddictConstants.TokenTypeIdentifiers.Private.DeviceCode; break;
            case "null-token-expiry": token.ExpirationDate = null; break;
            case "legacy-no-ledger": _db.Remove(ledger); break;
            case "ledger-future-bounds":
                ledger.AbsoluteExpiresUtc = _now.AddHours(2); ledger.SlidingExpiresUtc = _now.AddHours(1); break;
            case "ledger-revoked": ledger.RevokedUtc = _now.AddMinutes(-1); break;
            case "ledger-absolute-expired": ledger.AbsoluteExpiresUtc = _now.AddTicks(-1); break;
            case "ledger-sliding-expired": ledger.SlidingExpiresUtc = _now.AddTicks(-1); break;
            case "ledger-absolute-at-now": ledger.AbsoluteExpiresUtc = _now; break;
            case "ledger-sliding-at-now": ledger.SlidingExpiresUtc = _now; break;
            case "ledger-wrong-user": ledger.UserId = Guid.NewGuid(); break;
            case "ledger-wrong-client": ledger.ClientId = "other-client"; break;
            case "token-wrong-subject": token.Subject = Guid.NewGuid().ToString(); break;
            case "token-wrong-application":
                token.Application = new OpenIddictEntityFrameworkCoreApplication<Guid>
                    { Id = Guid.NewGuid(), ClientId = "other-client" };
                _db.Add(token.Application); break;
            case "token-no-application": token.Application = null; break;
            case "authorization-no-application": authorization.Application = null; break;
            case "missing-user": _db.Users.Remove(_db.Users.Local.Single()); break;
            case "no-subject": authorization.Subject = null; token.Subject = null; break;
        }
        // Historical successes and an unbacked ledger cannot inflate the count.
        _db.LoginHistories.AddRange(Enumerable.Range(0, 4).Select(_ => new LoginHistory
            { UserId = ledger.UserId, LoginTime = _now.AddMinutes(-2), IsSuccessful = true }));
        _db.UserSessions.Add(new UserSession
            { AuthorizationId = Guid.NewGuid().ToString(), UserId = ledger.UserId, ClientId = "orphan" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        foreach (var stats in await ReadBothStatsAsync())
        {
            Assert.Equal(expected, stats.ActiveSessions);
            Assert.Equal(4, stats.TotalLogins);
        }
    }

    [Fact]
    public async Task GetActivityStatsAsync_ShouldCountEachAuthorization_WhenOneUserHasSeveralGrants()
    {
        var (first, _, _) = AddSession();
        var second = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        {
            Id = Guid.NewGuid(), Subject = first.Subject, Application = first.Application,
            Status = OpenIddictConstants.Statuses.Valid, CreationDate = _now.AddDays(-30)
        };
        _db.Add(second);
        AddToken(second, true);
        AddToken(second);
        await _db.SaveChangesAsync();

        foreach (var stats in await ReadBothStatsAsync())
            Assert.Equal(2, stats.ActiveSessions);
    }

    [Fact]
    public async Task GetActivityStatsAsync_ShouldExcludeCookieOnlyLoginsAndDetachedTokens()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "cookie-only" };
        _db.Users.Add(user);
        _db.LoginHistories.Add(new LoginHistory
            { UserId = user.Id, LoginTime = _now.AddHours(-1), IsSuccessful = true });
        _db.Add(new OpenIddictEntityFrameworkCoreToken<Guid>
        {
            Id = Guid.NewGuid(), Subject = user.Id.ToString(), Status = OpenIddictConstants.Statuses.Valid,
            Type = OpenIddictConstants.TokenTypeIdentifiers.AccessToken, ExpirationDate = _now.AddHours(1)
        });
        await _db.SaveChangesAsync();

        foreach (var stats in await ReadBothStatsAsync())
        {
            Assert.Equal(0, stats.ActiveSessions);
            Assert.Equal(1, stats.TotalLogins);
        }
    }

    [Fact]
    public async Task GetActivityStatsAsync_ShouldCombineRetainedFailures_WithoutLosingUnknownUsersOrMergingAttempts()
    {
        var start = _now.AddHours(-24);
        var userId = Guid.NewGuid();
        _db.LoginHistories.AddRange(
            new LoginHistory { UserId = userId, IsSuccessful = true, LoginTime = start, RiskScore = 20 },
            new LoginHistory { UserId = userId, IsSuccessful = false, LoginTime = _now.AddMinutes(-1), RiskScore = 80 },
            new LoginHistory { UserId = userId, IsSuccessful = false, LoginTime = start.AddTicks(-1) },
            new LoginHistory { UserId = userId, IsSuccessful = false, LoginTime = _now },
            new LoginHistory { UserId = userId, IsSuccessful = true, LoginTime = _now.AddHours(1) });
        AddAudit("LoginAttempt", "Login attempt for user 'masked': failed (Invalid credentials)", start, null);
        AddAudit("LoginAttempt", "Login attempt for user 'masked': failed (Invalid credentials)", start, "");
        AddAudit("UserLogin", "{\"Success\":false}", _now.AddMinutes(-1), userId.ToString());
        // Equal user and timestamp remain separate retained failures, not fuzzy duplicates.
        AddAudit("UserLogin", "{\"Success\":false}", _now.AddMinutes(-1), userId.ToString());
        AddAudit("LoginAttempt", "{\"details\":\"Login attempt for user 'masked': failed (Blocked)\",\"impersonation\":{}}",
            _now.AddMinutes(-2), null);
        AddAudit("LoginAttempt", "Login attempt for user 'masked': successful", start, userId.ToString());
        AddAudit("UserLogin", "{\"Success\":true}", start, userId.ToString());
        AddAudit("UserLogin", "not-json", start, null);
        AddAudit("OtherEvent", "{\"Success\":false}", start, null);
        AddAudit("UserLogin", "{\"Success\":false}", start.AddTicks(-1), null);
        AddAudit("UserLogin", "{\"Success\":false}", _now, null);
        AddAudit("UserLogin", "{\"Success\":false}", _now.AddHours(1), null);
        await _db.SaveChangesAsync();

        foreach (var stats in await ReadBothStatsAsync())
        {
            Assert.Equal(6, stats.FailedLogins);
            Assert.Equal(1, stats.TotalLogins);
            Assert.Equal(0, stats.ActiveSessions);
        }
        // Audit-only failures supply no risk scores; the monitoring risk model is unchanged.
        Assert.Equal(50, (await _monitoring.GetActivityStatsAsync()).RiskScore);
    }

    [Fact]
    public async Task GetSecurityMetricsAsync_ShouldUseSameUtcDailyFailureAndAttemptBuckets_InBothServices()
    {
        var start = _now.Date.AddDays(-6);
        for (var day = 0; day < 7; day++)
        {
            var time = start.AddDays(day);
            for (var i = 0; i <= day; i++)
            {
                _db.LoginHistories.Add(new LoginHistory
                    { UserId = Guid.NewGuid(), IsSuccessful = true, LoginTime = time });
                AddAudit("UserLogin", "{\"Success\":false}", time, null);
                AddAudit("LoginAttempt", "Login attempt for user 'masked': successful", time, null);
            }
            _db.LoginHistories.Add(new LoginHistory
                { UserId = Guid.NewGuid(), IsSuccessful = false, LoginTime = time });
        }
        AddAudit("UserLogin", "{\"Success\":false}", start.AddTicks(-1), null);
        AddAudit("UserLogin", "{\"Success\":false}", _now, null);
        _db.LoginHistories.Add(new LoginHistory
            { UserId = Guid.NewGuid(), IsSuccessful = true, LoginTime = _now.AddMinutes(1) });
        await _db.SaveChangesAsync();

        foreach (var metrics in new[] { await _dashboard.GetSecurityMetricsAsync(), await _monitoring.GetSecurityMetricsAsync() })
        {
            Assert.Equal(Enumerable.Range(1, 7), metrics.ActiveSessions); // Legacy name: successful logins.
            Assert.Equal(Enumerable.Range(2, 7), metrics.FailedLogins);
            Assert.Equal(Enumerable.Range(1, 7).Select(success => 2 * success + 1), metrics.LoginAttempts);
        }
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgres")]
    public void EffectiveAuthorizations_ShouldTranslateEntirePredicate_WithoutOpeningDatabase(string provider)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (provider == "sqlserver")
            options.UseSqlServer("Server=unused.invalid;Database=statistics_translation;Integrated Security=true",
                sql => sql.UseCompatibilityLevel(150));
        else
            options.UseNpgsql("Host=unused.invalid;Database=statistics_translation;Username=unused");
        using var db = new ApplicationDbContext(options.Options);

        var sql = ActivityStatisticsReader.EffectiveAuthorizations(db, _now).ToQueryString();

        Assert.Contains("EXISTS", sql);
        Assert.Contains("OpenIddictTokens", sql);
        Assert.Contains("UserSessions", sql);
        Assert.Contains("ExpirationDate", sql);
        Assert.Contains("RevokedUtc", sql);
        Assert.DoesNotContain("OPENJSON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON_VALUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private async Task<ActivityStatsDto[]> ReadBothStatsAsync() =>
        [await _dashboard.GetActivityStatsAsync(), await _monitoring.GetActivityStatsAsync()];

    private (OpenIddictEntityFrameworkCoreAuthorization<Guid>, OpenIddictEntityFrameworkCoreToken<Guid>, UserSession) AddSession()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "session-user" };
        _db.Users.Add(user);
        var app = new OpenIddictEntityFrameworkCoreApplication<Guid> { Id = Guid.NewGuid(), ClientId = "statistics-client" };
        var authorization = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        {
            Id = Guid.NewGuid(), Subject = user.Id.ToString(), Application = app,
            Status = OpenIddictConstants.Statuses.Valid, CreationDate = _now.AddDays(-30)
        };
        _db.Add(authorization);
        var token = AddToken(authorization);
        var ledger = new UserSession
        {
            AuthorizationId = authorization.Id.ToString(), UserId = user.Id, ClientId = app.ClientId,
            CreatedUtc = _now.AddDays(-30), LastActivityUtc = _now.AddDays(-2)
        };
        _db.UserSessions.Add(ledger);
        return (authorization, token, ledger);
    }

    private OpenIddictEntityFrameworkCoreToken<Guid> AddToken(
        OpenIddictEntityFrameworkCoreAuthorization<Guid> authorization, bool refresh = false)
    {
        var token = new OpenIddictEntityFrameworkCoreToken<Guid>
        {
            Id = Guid.NewGuid(), Authorization = authorization, Application = authorization.Application,
            Subject = authorization.Subject, Status = OpenIddictConstants.Statuses.Valid,
            Type = refresh ? OpenIddictConstants.TokenTypeIdentifiers.RefreshToken : OpenIddictConstants.TokenTypeIdentifiers.AccessToken,
            CreationDate = _now.AddMinutes(-5), ExpirationDate = _now.AddMinutes(30)
        };
        _db.Add(token);
        return token;
    }

    private void AddAudit(string eventType, string details, DateTime timestamp, string? userId) =>
        _db.AuditEvents.Add(new AuditEvent { EventType = eventType, Details = details, Timestamp = timestamp, UserId = userId });

    public void Dispose() => _db.Dispose();
}
