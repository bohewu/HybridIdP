using System.Security.Claims;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class MfaEnrollmentSessionTests
{
    [Fact]
    public void CompletePending_BindsFreshProofToAuthenticatedUser()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var principal = CreatePrincipal(userId);

        MfaEnrollmentSession.Begin(session, timeProvider);

        Assert.True(MfaEnrollmentSession.CompletePending(session, principal, timeProvider));
        Assert.True(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, Guid.NewGuid(), timeProvider));
    }

    [Fact]
    public void HasPending_ReturnsTrueOnlyWhileReauthenticationAttemptIsActive()
    {
        var session = new MemorySession();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

        Assert.False(MfaEnrollmentSession.HasPending(session, timeProvider));

        MfaEnrollmentSession.Begin(session, timeProvider);
        Assert.True(MfaEnrollmentSession.HasPending(session, timeProvider));

        timeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.False(MfaEnrollmentSession.HasPending(session, timeProvider));
    }

    [Fact]
    public void CompletePending_RejectsExpiredReauthenticationAttempt()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

        MfaEnrollmentSession.Begin(session, timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.False(
            MfaEnrollmentSession.CompletePending(
                session,
                CreatePrincipal(userId),
                timeProvider));
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));
    }

    [Fact]
    public void HasFreshProof_RejectsExpiredOrConsumedProof()
    {
        var session = new MemorySession();
        var userId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

        MfaEnrollmentSession.Begin(session, timeProvider);
        Assert.True(
            MfaEnrollmentSession.CompletePending(
                session,
                CreatePrincipal(userId),
                timeProvider));

        MfaEnrollmentSession.Consume(session);
        Assert.False(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));

        MfaEnrollmentSession.Begin(session, timeProvider);
        Assert.True(
            MfaEnrollmentSession.CompletePending(
                session,
                CreatePrincipal(userId),
                timeProvider));
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.False(MfaEnrollmentSession.HasFreshProof(session, userId, timeProvider));
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "test"));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
