using System.Text.Json;
using Core.Domain;

namespace Web.IdP.Helpers;

public static class RequiredPasswordChangeSession
{
    private const string SessionKey = "required-password-change.pending";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static void Begin(
        ISession session,
        ApplicationUser user,
        string? returnUrl,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user.SecurityStamp);
        session.SetString(
            SessionKey,
            JsonSerializer.Serialize(new PendingChange(
                user.Id,
                user.SecurityStamp,
                returnUrl,
                now.Add(Lifetime),
                RequiredPasswordChangeAuthority.Local,
                null)));
    }

    public static void BeginDirectory(
        ISession session,
        ApplicationUser user,
        Guid directoryObjectId,
        string? returnUrl,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user.SecurityStamp);
        if (directoryObjectId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(directoryObjectId));
        session.SetString(SessionKey, JsonSerializer.Serialize(new PendingChange(
            user.Id, user.SecurityStamp, returnUrl, now.Add(Lifetime),
            RequiredPasswordChangeAuthority.Directory, directoryObjectId)));
    }

    public static bool TryRead(
        ISession session,
        DateTimeOffset now,
        out PendingRequiredPasswordChange pending)
    {
        var value = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            pending = default!;
            return false;
        }

        PendingChange? stored;
        try
        {
            stored = JsonSerializer.Deserialize<PendingChange>(value);
        }
        catch (JsonException)
        {
            stored = null;
        }

        if (stored is null || stored.ExpiresAtUtc <= now)
        {
            session.Remove(SessionKey);
            pending = default!;
            return false;
        }

        pending = new PendingRequiredPasswordChange(
            stored.UserId,
            stored.SecurityStamp,
            stored.ReturnUrl,
            stored.Authority,
            stored.DirectoryObjectId);
        return true;
    }

    public static bool TryConsume(
        ISession session,
        DateTimeOffset now,
        out PendingRequiredPasswordChange pending)
    {
        var found = TryRead(session, now, out pending);
        session.Remove(SessionKey);
        return found;
    }

    public static void Clear(ISession session) => session.Remove(SessionKey);

    private sealed record PendingChange(
        Guid UserId,
        string SecurityStamp,
        string? ReturnUrl,
        DateTimeOffset ExpiresAtUtc,
        RequiredPasswordChangeAuthority Authority,
        Guid? DirectoryObjectId);
}

public sealed record PendingRequiredPasswordChange(
    Guid UserId,
    string SecurityStamp,
    string? ReturnUrl,
    RequiredPasswordChangeAuthority Authority,
    Guid? DirectoryObjectId);

public enum RequiredPasswordChangeAuthority
{
    Local,
    Directory
}
