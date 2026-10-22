using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain.Constants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

// Email evidence has no identity-binding authority and is never exposed through generic settings.
public sealed class NeutralMetadataEvidenceStore(IApplicationDbContext db, INeutralQueryService queries,
    IDataProtectionProvider protection, TimeProvider? timeProvider = null) : INeutralMetadataEvidenceReader
{
    private const string Prefix = "neutralQuery.metadataEvidence.";
    private readonly IDataProtector _protector = protection.CreateProtector("NeutralQuery.MetadataEvidence.v2");
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<NeutralMetadataEvidenceResult> ReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var resolution = await queries.ResolveAsync("metadata", userId, cancellationToken: cancellationToken);
        if (resolution.Status == "Legacy") return await ReadLegacyAsync(userId, cancellationToken);
        if (resolution.Status != "Ready" || resolution.Context is not { } context) return new(true, []);
        var key = Key(userId, context.SourceId);
        var value = await db.Settings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).SingleOrDefaultAsync(cancellationToken);
        if (value is null) return new(true, []);
        try
        {
            var stored = JsonSerializer.Deserialize<StoredEvidence>(_protector.Unprotect(value));
            if (stored is null || stored.UserId != userId || stored.SourceId != context.SourceId ||
                stored.Evidence.QueryFingerprint != context.Fingerprint || !await queries.IsCurrentAsync(context, cancellationToken))
                return new(true, []);
            return new(true, [stored.Evidence]);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return new(true, []); }
    }

    public async Task<Guid?> LegacyRevisionAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().AnyAsync(x => x.Key == SettingKeys.NeutralQueryConfiguration, ct)
            ? (await queries.GetConfigurationAsync(ct)).Revision : null;

    public async Task ConfirmLegacyAsync(Guid userId, Guid bindingId, DateTimeOffset refreshedAt, Guid? revision, CancellationToken ct)
    {
        if (revision is null || revision != await LegacyRevisionAsync(ct)) return;
        var key = LegacyKey(userId);
        var row = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) { row = new Setting { Id = Guid.NewGuid(), Key = key }; db.Settings.Add(row); }
        row.Value = _protector.Protect(JsonSerializer.Serialize(new LegacyConfirmation(userId, bindingId, refreshedAt, revision.Value)));
        row.UpdatedUtc = _time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    private async Task<NeutralMetadataEvidenceResult> ReadLegacyAsync(Guid userId, CancellationToken ct)
    {
        var revision = await LegacyRevisionAsync(ct);
        if (revision is null) return new(false, []); // Untouched legacy deployments keep their original evidence behavior.
        var key = LegacyKey(userId);
        var value = await db.Settings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).SingleOrDefaultAsync(ct);
        if (value is null) return new(true, []);
        try
        {
            var stamp = JsonSerializer.Deserialize<LegacyConfirmation>(_protector.Unprotect(value));
            if (stamp is null || stamp.UserId != userId || stamp.Revision != revision) return new(true, []);
            if (!await db.ProviderSubjectDirectoryBindings.AsNoTracking().AnyAsync(x => x.Id == stamp.BindingId && x.LocalAccountId == userId, ct))
                return new(true, []);
            var row = await db.ProviderMetadataSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderSubjectDirectoryBindingId == stamp.BindingId, ct);
            if (row is null || row.RefreshedAtUtc != stamp.RefreshedAtUtc || revision != await LegacyRevisionAsync(ct)) return new(true, []);
            return new(true, [new(row.Id.ToString("D"), revision.ToString(), row.EvidenceState, row.Email,
                row.EmailTrustOrigin, row.VerifiedAt, row.RefreshedAtUtc)]);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return new(true, []); }
    }

    public async Task<bool> SaveAsync(NeutralQueryContext context, NeutralMetadataResult? result,
        ProviderMetadataEvidenceState failure, CancellationToken ct)
    {
        if (!await queries.IsCurrentAsync(context, ct)) return false;
        var now = _time.GetUtcNow();
        var trusted = result is not null && result.EmailTrustOrigin is "SourceVerified" or "PolicyTrusted" &&
            !string.IsNullOrWhiteSpace(result.Email) && (!result.VerifiedAt.HasValue || result.VerifiedAt <= now);
        var evidence = new MetadataEmailEvidence(context.SourceId, context.Fingerprint,
            result is null ? failure : trusted ? ProviderMetadataEvidenceState.Available : ProviderMetadataEvidenceState.Untrusted,
            trusted ? result!.Email!.Trim() : null,
            trusted ? Enum.Parse<ProviderEmailTrustOrigin>(result!.EmailTrustOrigin) : ProviderEmailTrustOrigin.Unknown,
            trusted && result!.EmailTrustOrigin == "SourceVerified" ? result.VerifiedAt : null, now);
        var key = Key(context.LocalAccountId, context.SourceId);
        var row = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) { row = new Setting { Id = Guid.NewGuid(), Key = key }; db.Settings.Add(row); }
        row.Value = _protector.Protect(JsonSerializer.Serialize(new StoredEvidence(context.LocalAccountId, context.SourceId, evidence)));
        row.UpdatedUtc = now.UtcDateTime;
        await db.SaveChangesAsync(ct);
        return trusted;
    }

    private static string Key(Guid userId, string sourceId) => Prefix + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { userId, sourceId }))));
    private sealed record StoredEvidence(Guid UserId, string SourceId, MetadataEmailEvidence Evidence);
    private static string LegacyKey(Guid userId) => Prefix + "legacy." + userId.ToString("N");
    private sealed record LegacyConfirmation(Guid UserId, Guid BindingId, DateTimeOffset RefreshedAtUtc, Guid Revision);
}
