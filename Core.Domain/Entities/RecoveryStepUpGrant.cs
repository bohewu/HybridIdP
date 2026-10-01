namespace Core.Domain.Entities;

/// <summary>One fresh interactive authentication completion for recovery preference mutation.</summary>
public sealed class RecoveryStepUpGrant
{
    private RecoveryStepUpGrant() { }

    public RecoveryStepUpGrant(Guid localAccountId, string securityStamp, string contextHash,
        string csrfHash, string authorityBinding, DateTimeOffset authenticatedAtUtc, DateTimeOffset expiresAtUtc)
    {
        RecoveryStateGuard.Account(localAccountId);
        RecoveryStateGuard.Text(securityStamp, 256);
        RecoveryStateGuard.Text(contextHash, 256);
        RecoveryStateGuard.Text(csrfHash, 256);
        RecoveryStateGuard.Text(authorityBinding, 64);
        RecoveryStateGuard.Lifetime(authenticatedAtUtc, expiresAtUtc);
        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        SecurityStamp = securityStamp;
        ContextHash = contextHash;
        CsrfHash = csrfHash;
        AuthorityBinding = authorityBinding;
        AuthenticatedAtUtc = authenticatedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public string ContextHash { get; private set; } = string.Empty;
    public string CsrfHash { get; private set; } = string.Empty;
    public string AuthorityBinding { get; private set; } = string.Empty;
    public DateTimeOffset AuthenticatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;

    public bool TryConsume(Guid localAccountId, string securityStamp, string contextHash,
        string csrfHash, string authorityBinding, DateTimeOffset now)
    {
        if (LocalAccountId != localAccountId || SecurityStamp != securityStamp || ContextHash != contextHash ||
            CsrfHash != csrfHash || AuthorityBinding != authorityBinding || AuthenticatedAtUtc > now ||
            ExpiresAtUtc <= now || ConsumedAtUtc is not null || RevokedAtUtc is not null) return false;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (ConsumedAtUtc is not null || RevokedAtUtc is not null) return;
        RevokedAtUtc = now;
        Version++;
    }
}
