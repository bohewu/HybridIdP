using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Web.IdP.UnitTests.Helpers;

public sealed class DeviceVerificationSessionTests
{
    [Fact]
    public void TryConsume_MatchingUserAndInteraction_SucceedsOnlyOnce()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var interaction = CreateInteraction("client-1", "ABCD-EFGH");
        var token = DeviceVerificationSession.Issue(
            session,
            principal,
            interaction);

        Assert.True(DeviceVerificationSession.TryConsume(
            session,
            principal,
            interaction,
            token));
        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            principal,
            interaction,
            token));
    }

    [Fact]
    public void TryConsume_DifferentInteraction_RejectsAndConsumesIntent()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var original = CreateInteraction("client-1", "ABCD-EFGH");
        var changed = CreateInteraction("client-1", "WXYZ-1234");
        var token = DeviceVerificationSession.Issue(
            session,
            principal,
            original);

        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            principal,
            changed,
            token));
        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            principal,
            original,
            token));
    }

    [Fact]
    public void TryConsume_DifferentUser_RejectsAndConsumesIntent()
    {
        var session = new MemorySession();
        var originalPrincipal = CreatePrincipal("user-1");
        var interaction = CreateInteraction("client-1", "ABCD-EFGH");
        var token = DeviceVerificationSession.Issue(
            session,
            originalPrincipal,
            interaction);

        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            CreatePrincipal("user-2"),
            interaction,
            token));
        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            originalPrincipal,
            interaction,
            token));
    }

    [Fact]
    public void TryConsume_ManualEntryIntent_AcceptsResolvedInteractionOnlyOnce()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var token = DeviceVerificationSession.Issue(
            session,
            principal,
            AuthenticateResult.NoResult());
        var interaction = CreateInteraction("client-1", "ABCD-EFGH");

        Assert.True(DeviceVerificationSession.TryConsume(
            session,
            principal,
            interaction,
            token));
        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            principal,
            interaction,
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
                var interaction = CreateInteraction(
                    "client-1",
                    $"CODE-{index:0000}");
                var token = DeviceVerificationSession.Issue(
                    session,
                    principal,
                    interaction);
                return (interaction, token);
            })
            .ToArray();

        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            principal,
            issued[0].interaction,
            issued[0].token));
        foreach (var (interaction, token) in issued.Skip(1))
        {
            Assert.True(DeviceVerificationSession.TryConsume(
                session,
                principal,
                interaction,
                token));
        }
    }

    [Fact]
    public void TryConsume_ExpiredIntent_ReturnsFalse()
    {
        var session = new MemorySession();
        var principal = CreatePrincipal("user-1");
        var interaction = CreateInteraction("client-1", "ABCD-EFGH");
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var token = DeviceVerificationSession.Issue(
            session,
            principal,
            interaction,
            timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.False(DeviceVerificationSession.TryConsume(
            session,
            principal,
            interaction,
            token,
            timeProvider));
    }

    private static ClaimsPrincipal CreatePrincipal(string subject)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Claims.Subject, subject)],
            "test"));
    }

    private static AuthenticateResult CreateInteraction(
        string clientId,
        string userCode)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Claims.ClientId, clientId)],
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken
            {
                Name = OpenIddictServerAspNetCoreConstants.Tokens.UserCode,
                Value = userCode
            }
        ]);
        return AuthenticateResult.Success(new AuthenticationTicket(
            principal,
            properties,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
