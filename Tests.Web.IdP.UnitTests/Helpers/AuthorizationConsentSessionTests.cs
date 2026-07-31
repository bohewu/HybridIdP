using System.Security.Claims;
using OpenIddict.Abstractions;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Web.IdP.UnitTests.Helpers;

public sealed class AuthorizationConsentSessionTests
{
    [Fact]
    public void TryConsume_MatchingUserAndRequest_SucceedsOnlyOnce()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var request = CreateRequest(state: "state-1");
        var token = AuthorizationConsentSession.Issue(session, principal, request);

        Assert.True(AuthorizationConsentSession.TryConsume(
            session,
            principal,
            request,
            token));
        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            principal,
            request,
            token));
    }

    [Fact]
    public void TryConsume_ChangedAuthorizationRequest_RejectsAndConsumesIntent()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var originalRequest = CreateRequest(state: "state-1");
        var changedRequest = CreateRequest(state: "state-2");
        var token = AuthorizationConsentSession.Issue(
            session,
            principal,
            originalRequest);

        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            principal,
            changedRequest,
            token));
        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            principal,
            originalRequest,
            token));
    }

    [Fact]
    public void TryConsume_DifferentUser_RejectsAndConsumesIntent()
    {
        var session = new MemorySession();
        var originalPrincipal = CreatePrincipal("user-1");
        var request = CreateRequest(state: "state-1");
        var token = AuthorizationConsentSession.Issue(
            session,
            originalPrincipal,
            request);

        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            CreatePrincipal("user-2"),
            request,
            token));
        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            originalPrincipal,
            request,
            token));
    }

    [Fact]
    public void Issue_MoreThanMaximumPendingIntents_EvictsOldestOnly()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var issued = Enumerable.Range(0, 9)
            .Select(index =>
            {
                var request = CreateRequest(state: $"state-{index}");
                var token = AuthorizationConsentSession.Issue(
                    session,
                    principal,
                    request);
                return (request, token);
            })
            .ToArray();

        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            principal,
            issued[0].request,
            issued[0].token));
        foreach (var (request, token) in issued.Skip(1))
        {
            Assert.True(AuthorizationConsentSession.TryConsume(
                session,
                principal,
                request,
                token));
        }
    }

    [Fact]
    public void TryConsume_ExpiredIntent_ReturnsFalse()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var request = CreateRequest(state: "state-1");
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var token = AuthorizationConsentSession.Issue(
            session,
            principal,
            request,
            timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.False(AuthorizationConsentSession.TryConsume(
            session,
            principal,
            request,
            token,
            timeProvider));
    }

    private static ClaimsPrincipal CreatePrincipal(string subject)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Claims.Subject, subject)],
            "test"));
    }

    private static OpenIddictRequest CreateRequest(string state)
    {
        return new OpenIddictRequest
        {
            ClientId = "testclient-public",
            RedirectUri = "https://client.example/callback",
            ResponseType = ResponseTypes.Code,
            Scope = "openid profile",
            State = state,
            CodeChallenge = "test-code-challenge",
            CodeChallengeMethod = CodeChallengeMethods.Sha256
        };
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
