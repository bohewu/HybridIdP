using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryEmailStepUpService(ApplicationDbContext db,
    IOptions<RecoveryEmailSelectionOptions> options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public bool Enabled => options.Value.Enabled && options.Value.SelfServiceEnabled;

    public async Task<RecoveryAuthenticationState?> ResolveAsync(Guid accountId, CancellationToken ct = default)
    {
        if (!Enabled) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == accountId, ct);
        var now = _time.GetUtcNow();
        if (user is null || !user.IsActive || user.IsDeleted || user.RequiresPasswordChange ||
            string.IsNullOrWhiteSpace(user.SecurityStamp) || user.LockoutEnabled && user.LockoutEnd > now) return null;
        if (user.PersonId is { } personId &&
            (await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, ct))?.CanAuthenticate() != true) return null;
        if (await new NativeDirectoryRecoveryBarrier(db).HasIssuanceBarrierAsync(accountId, ct)) return null;
        var binding = await db.ProviderSubjectDirectoryBindings.AsNoTracking().SingleOrDefaultAsync(b => b.LocalAccountId == accountId, ct);
        var migration = await db.CredentialMigrationStateRecords.AsNoTracking().SingleOrDefaultAsync(m => m.LocalAccountId == accountId, ct);
        string authority;
        string? provider = null;
        string? providerKey = null;
        if (binding is not null || migration is not null)
        {
            if (binding is null || migration?.State != CredentialMigrationState.LocalFinalized ||
                migration.ProviderSubjectDirectoryBindingId != binding.Id) return null;
            authority = "directory";
        }
        else if (!string.IsNullOrEmpty(user.PasswordHash)) authority = "local";
        else
        {
            var logins = await db.UserLogins.AsNoTracking().Where(l => l.UserId == accountId).Take(2).ToListAsync(ct);
            if (logins.Count != 1) return null;
            authority = "external";
            provider = logins[0].LoginProvider;
            providerKey = logins[0].ProviderKey;
        }
        var digest = RecoveryDestinationBinding.ComputeDigest("recovery-stepup-authority-v1", accountId.ToString("D"),
            authority, binding?.Id.ToString("D"), binding?.ProviderNamespace, binding?.StableSubject,
            binding?.DirectoryObjectId.ToString("D"), provider, providerKey);
        return new(user, authority, digest, provider, providerKey);
    }

    // Only trusted authentication completion hooks call this server-only method.
    public async Task<Guid?> IssueAsync(Guid accountId, string expectedStamp, string expectedAuthority,
        string contextHash, string csrfHash, DateTimeOffset primaryAtUtc, CancellationToken ct = default)
    {
        var state = await ResolveAsync(accountId, ct);
        var now = _time.GetUtcNow();
        var expires = primaryAtUtc.AddMinutes(options.Value.RecentStepUpMinutes);
        if (state is null || state.User.SecurityStamp != expectedStamp || state.Binding != expectedAuthority ||
            primaryAtUtc > now || expires <= now || string.IsNullOrWhiteSpace(contextHash) || string.IsNullOrWhiteSpace(csrfHash)) return null;
        var grant = new RecoveryStepUpGrant(accountId, expectedStamp, contextHash, csrfHash, expectedAuthority, primaryAtUtc, expires);
        db.RecoveryStepUpGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        return grant.Id;
    }

    public async Task<RecoveryStepUpGrant?> ValidateAsync(Guid accountId, RecoveryPreferenceContext context,
        bool allowConsumed, CancellationToken ct = default)
    {
        var state = await ResolveAsync(accountId, ct);
        if (state is null) return null;
        var grant = await db.RecoveryStepUpGrants.SingleOrDefaultAsync(g => g.Id == context.StepUpGrantId, ct);
        var now = _time.GetUtcNow();
        return grant is not null && grant.LocalAccountId == accountId && grant.SecurityStamp == state.User.SecurityStamp &&
            grant.AuthorityBinding == state.Binding && grant.ContextHash == context.ContextHash && grant.CsrfHash == context.CsrfHash &&
            grant.AuthenticatedAtUtc <= now && grant.AuthenticatedAtUtc.AddMinutes(options.Value.RecentStepUpMinutes) > now &&
            grant.ExpiresAtUtc > now && grant.RevokedAtUtc is null && (allowConsumed || grant.ConsumedAtUtc is null) ? grant : null;
    }
}

public sealed record RecoveryAuthenticationState(ApplicationUser User, string Authority, string Binding,
    string? ExternalProvider, string? ExternalKey)
{
    public override string ToString() => nameof(RecoveryAuthenticationState);
}
