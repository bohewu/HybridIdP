using Core.Domain;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public sealed class RequiredPasswordChangeSessionTests
{
    [Fact]
    public void TryConsume_ValidState_IsUserBoundAndOneUse()
    {
        var now = new DateTimeOffset(2026, 9, 10, 1, 0, 0, TimeSpan.Zero);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            SecurityStamp = "security-stamp"
        };
        var session = new MemorySession();
        RequiredPasswordChangeSession.Begin(session, user, "/connect/authorize", now);

        Assert.True(RequiredPasswordChangeSession.TryConsume(session, now, out var pending));
        Assert.Equal(user.Id, pending.UserId);
        Assert.Equal(user.SecurityStamp, pending.SecurityStamp);
        Assert.Equal("/connect/authorize", pending.ReturnUrl);
        Assert.False(RequiredPasswordChangeSession.TryConsume(session, now, out _));
    }

    [Fact]
    public void TryRead_ExpiredState_RemovesIt()
    {
        var now = new DateTimeOffset(2026, 9, 10, 1, 0, 0, TimeSpan.Zero);
        var session = new MemorySession();
        RequiredPasswordChangeSession.Begin(
            session,
            new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "stamp" },
            null,
            now);

        Assert.False(RequiredPasswordChangeSession.TryRead(session, now.AddMinutes(11), out _));
        Assert.False(RequiredPasswordChangeSession.TryRead(session, now, out _));
    }

    [Fact]
    public void TryConsume_DirectoryState_PreservesAuthorityAndImmutableObject()
    {
        var now = new DateTimeOffset(2026, 9, 10, 1, 0, 0, TimeSpan.Zero);
        var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "stamp" };
        var directoryObjectId = Guid.NewGuid();
        var session = new MemorySession();

        RequiredPasswordChangeSession.BeginDirectory(session, user, directoryObjectId, null, now);

        Assert.True(RequiredPasswordChangeSession.TryConsume(session, now, out var pending));
        Assert.Equal(RequiredPasswordChangeAuthority.Directory, pending.Authority);
        Assert.Equal(directoryObjectId, pending.DirectoryObjectId);
    }
}
