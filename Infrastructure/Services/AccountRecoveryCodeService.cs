using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed class AccountRecoveryCodeService(ApplicationDbContext db, IMfaService? mfa = null) : IAccountRecoveryCodeService
{
    public const string LoginProvider = "HybridIdP.AccountRecoveryCodes";
    private const string BatchName = "Batch.v1";
    internal const string NativeProvider = "[AspNetUserStore]";
    internal const string NativeName = "RecoveryCodes";
    private (Guid UserId, string Stamp, string Operation)? _redemption;

    public async Task<AccountRecoveryCodeStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var batch = await db.UserTokens.AsNoTracking().SingleOrDefaultAsync(token =>
            token.UserId == userId && token.LoginProvider == LoginProvider && token.Name == BatchName, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == userId, ct);
        var native = await db.UserTokens.AsNoTracking().SingleOrDefaultAsync(token =>
            token.UserId == userId && token.LoginProvider == NativeProvider && token.Name == NativeName, ct);
        var legacyHistory = user?.RecoveryCodes != null || native != null;
        var legacyCount = user is { TwoFactorEnabled: true } or { EmailMfaEnabled: true }
            ? ReadHashes(user.RecoveryCodes).Count + (native?.Value?.Split(';', StringSplitOptions.RemoveEmptyEntries).Length ?? 0)
            : 0;
        return new(batch != null || legacyHistory, ReadHashes(batch?.Value).Count, legacyCount, legacyHistory);
    }

    public Task<IReadOnlyList<string>?> GenerateAsync(ApplicationUser user, CancellationToken ct = default) =>
        WriteBatchAsync(user, ct);

    private async Task<IReadOnlyList<string>?> WriteBatchAsync(ApplicationUser user, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var committed = false;
        try
        {
            if (!await IsCurrentAsync(user, ct)) return null;
            if (db.Entry(user).State == EntityState.Detached) db.Users.Attach(user);
            var codes = await StageBatchAsync(user.Id, onlyIfMissing: false, ct);
            // Retain tombstones: exhausted or retired legacy batches must suppress automatic issuance.
            if (user.RecoveryCodes != null) user.RecoveryCodes = "[]";
            var native = await db.UserTokens.SingleOrDefaultAsync(token => token.UserId == user.Id &&
                token.LoginProvider == NativeProvider && token.Name == NativeName, ct);
            if (native != null) native.Value = string.Empty;
            AdvanceConcurrency(user);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            committed = true;
            return codes;
        }
        catch (DbUpdateException) { return null; }
        finally
        {
            if (!committed)
            {
                if (transaction != null) await transaction.RollbackAsync(CancellationToken.None);
                await DiscardChangesAsync(user);
            }
        }
    }

    public async Task<bool> RedeemAsync(ApplicationUser user, string code, string operation, CancellationToken ct = default)
    {
        _redemption = null;
        var normalized = Normalize(code);
        if (string.IsNullOrEmpty(operation)) return false;
        if (normalized == null)
        {
            if (mfa == null) return false;
            var legacy = code.Replace(" ", "").Replace("-", "");
            if (legacy.Length != 10) return false;
            // Legacy OTP backups remain OTP-only and never create independent enrollment proof.
            return await mfa.ValidateRecoveryCodeAsync(user, legacy, ct) ||
                await mfa.ValidateNativeRecoveryCodeAsync(user, legacy, ct);
        }
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var committed = false;
        try
        {
            if (!await IsCurrentAsync(user, ct)) return false;
            var batch = await db.UserTokens.SingleOrDefaultAsync(token => token.UserId == user.Id &&
                token.LoginProvider == LoginProvider && token.Name == BatchName, ct);
            var hashes = ReadHashes(batch?.Value);
            if (batch == null || !hashes.Remove(Hash(user.Id, normalized))) return false;
            batch.Value = JsonSerializer.Serialize(hashes);
            AdvanceConcurrency(user);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            committed = true;
            _redemption = (user.Id, user.SecurityStamp!, operation);
            return true;
        }
        catch (DbUpdateException) { return false; }
        finally
        {
            if (!committed)
            {
                if (transaction != null) await transaction.RollbackAsync(CancellationToken.None);
                // AccessFailedAsync can save this same context immediately afterward.
                await DiscardChangesAsync(user);
            }
        }
    }

    public bool HasRedemptionProof(ApplicationUser user, string operation) =>
        _redemption is { } proof && proof.UserId == user.Id && proof.Stamp == user.SecurityStamp &&
        proof.Operation == operation;

    // Only registration calls this staging method. The enclosing credential transaction
    // owns the common user-version write, rollback, and disclosure after commit.
    internal async Task<IReadOnlyList<string>?> StageInitialBatchAsync(ApplicationUser user, CancellationToken ct)
    {
        // Query persisted factor state before the new enrollment is saved. Disabled credentials
        // and empty legacy stores are history, not grounds to silently create another batch.
        if (user.RecoveryCodes != null ||
            await db.Users.AsNoTracking().AnyAsync(value => value.Id == user.Id &&
                (value.TwoFactorEnabled || value.EmailMfaEnabled || value.RecoveryCodes != null), ct) ||
            await db.UserCredentials.AsNoTracking().AnyAsync(value => value.UserId == user.Id, ct) ||
            await db.UserTokens.AnyAsync(token => token.UserId == user.Id &&
                token.LoginProvider == NativeProvider && token.Name == NativeName, ct)) return null;
        return await StageBatchAsync(user.Id, onlyIfMissing: true, ct);
    }

    private async Task<IReadOnlyList<string>?> StageBatchAsync(Guid userId, bool onlyIfMissing, CancellationToken ct)
    {
        var batch = await db.UserTokens.SingleOrDefaultAsync(token => token.UserId == userId &&
            token.LoginProvider == LoginProvider && token.Name == BatchName, ct);
        if (batch != null && onlyIfMissing) return null;
        var codes = Enumerable.Range(0, 10).Select(_ =>
        {
            var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            return string.Join("-", Enumerable.Range(0, 8).Select(index => hex.Substring(index * 4, 4)));
        }).ToArray();
        if (batch == null)
        {
            batch = new IdentityUserToken<Guid> { UserId = userId, LoginProvider = LoginProvider, Name = BatchName };
            db.UserTokens.Add(batch);
        }
        batch.Value = JsonSerializer.Serialize(codes.Select(code => Hash(userId, Normalize(code)!)).ToArray());
        return codes;
    }

    internal async Task DiscardChangesAsync(ApplicationUser user)
    {
        foreach (var entry in db.ChangeTracker.Entries<IdentityUserToken<Guid>>()
                     .Where(entry => entry.Entity.UserId == user.Id && (entry.Entity.LoginProvider == LoginProvider ||
                         entry.Entity.LoginProvider == NativeProvider)).ToArray())
            entry.State = EntityState.Detached;
        var userEntry = db.Entry(user);
        if (userEntry.State == EntityState.Detached) return;
        try { await userEntry.ReloadAsync(CancellationToken.None); }
        catch { userEntry.State = EntityState.Detached; throw; }
    }

    private Task<bool> IsCurrentAsync(ApplicationUser user, CancellationToken ct) =>
        string.IsNullOrEmpty(user.SecurityStamp) ? Task.FromResult(false) :
        db.Users.AsNoTracking().AnyAsync(value => value.Id == user.Id && value.SecurityStamp == user.SecurityStamp &&
            value.ConcurrencyStamp == user.ConcurrencyStamp && value.IsActive && !value.IsDeleted &&
            !value.RequiresPasswordChange, ct);

    private void AdvanceConcurrency(ApplicationUser user)
    {
        if (db.Entry(user).State == EntityState.Detached) db.Users.Attach(user);
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        db.Entry(user).Property(value => value.ConcurrencyStamp).IsModified = true;
    }

    private static string Hash(Guid userId, string normalized) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(LoginProvider + ":v1:" + userId.ToString("N") + ":" + normalized)));

    private static string? Normalize(string code)
    {
        var normalized = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        return normalized.Length == 32 && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }

    private static List<string> ReadHashes(string? value)
    {
        if (value == null) return [];
        try { return JsonSerializer.Deserialize<List<string>>(value) ?? []; }
        catch (JsonException) { return []; }
    }
}
