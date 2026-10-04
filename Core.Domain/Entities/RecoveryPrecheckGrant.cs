namespace Core.Domain.Entities;

/// <summary>Server-held precheck state. Contains no submitted identity evidence or derived hash.</summary>
public sealed class RecoveryPrecheckGrant
{
    private RecoveryPrecheckGrant() { }

    public RecoveryPrecheckGrant(Guid localAccountId, Guid? providerBindingId, string? providerBindingVersion,
        string securityStamp, string contextHash, string csrfHash, string effectivePolicyVersion,
        long selectionEpoch, RecoveryDestinationKind destinationKind, string destinationFingerprint,
        long destinationVersion, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        RecoveryStateGuard.Account(localAccountId);
        if (providerBindingId == Guid.Empty || providerBindingId.HasValue != (providerBindingVersion is not null))
            throw new ArgumentException("The provider binding must be complete or absent for an authorized local cohort.");
        if (providerBindingVersion is not null) RecoveryStateGuard.Text(providerBindingVersion, 64);
        RecoveryStateGuard.Text(securityStamp, 256);
        RecoveryStateGuard.Text(contextHash, 256);
        RecoveryStateGuard.Text(csrfHash, 256);
        RecoveryStateGuard.Text(effectivePolicyVersion, 64);
        RecoveryStateGuard.Selection(selectionEpoch, destinationKind, destinationFingerprint, destinationVersion);
        RecoveryStateGuard.Lifetime(createdAtUtc, expiresAtUtc);
        Id = Guid.NewGuid();
        LocalAccountId = localAccountId;
        ProviderBindingId = providerBindingId;
        ProviderBindingVersion = providerBindingVersion;
        SecurityStamp = securityStamp;
        ContextHash = contextHash;
        CsrfHash = csrfHash;
        EffectivePolicyVersion = effectivePolicyVersion;
        SelectionEpoch = selectionEpoch;
        DestinationKind = destinationKind;
        DestinationFingerprint = destinationFingerprint;
        DestinationVersion = destinationVersion;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid LocalAccountId { get; private set; }
    public Guid? ProviderBindingId { get; private set; }
    public string? ProviderBindingVersion { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public string ContextHash { get; private set; } = string.Empty;
    public string CsrfHash { get; private set; } = string.Empty;
    public string EffectivePolicyVersion { get; private set; } = string.Empty;
    public long SelectionEpoch { get; private set; }
    public RecoveryDestinationKind DestinationKind { get; private set; }
    public string DestinationFingerprint { get; private set; } = string.Empty;
    public long DestinationVersion { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReservedChallengeId { get; private set; }
    public long Version { get; private set; } = 1;

    // Save this CAS and the challenge in the same relational transaction before SMTP.
    public bool TryReserveChallenge(RecoveryProofChallenge challenge, DateTimeOffset now)
    {
        if (ConsumedAtUtc is not null || RevokedAtUtc is not null || ExpiresAtUtc <= now ||
            challenge.LocalAccountId != LocalAccountId || challenge.Purpose != RecoveryProofPurpose.NativePasswordRecovery ||
            challenge.SelectionEpoch != SelectionEpoch || challenge.DestinationKind != DestinationKind ||
            challenge.DestinationFingerprint != DestinationFingerprint || challenge.DestinationVersion != DestinationVersion ||
            challenge.NativeSecurityStamp != SecurityStamp || challenge.NativeContextHash != ContextHash ||
            challenge.NativeCsrfHash != CsrfHash || challenge.ConsumedAtUtc is not null || challenge.RevokedAtUtc is not null ||
            challenge.VerifiedAtUtc is not null || challenge.ExpiresAtUtc <= now) return false;
        challenge.ReserveDelivery();
        ReservedChallengeId = challenge.Id;
        ConsumedAtUtc = now;
        Version++;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAtUtc is not null || ConsumedAtUtc is not null) return;
        RevokedAtUtc = now;
        Version++;
    }
}
