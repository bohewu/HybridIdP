using System.Diagnostics;
using System.Globalization;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryPrecheckService(ApplicationDbContext db, IRecoveryDestinationResolver resolver,
    IRecoveryIdentityVerificationClient verifier, RecoveryThrottleService throttle, RecoveryOtpDeliveryService delivery,
    IPasswordHasher<ApplicationUser> hasher, ILookupNormalizer normalizer, IForgotPasswordRoutingEvaluator routing,
    IOptions<RecoveryIdentityVerificationOptions> identityOptions, IOptions<RecoveryEmailSelectionOptions> selectionOptions,
    IOptions<ForgotPasswordRecoveryOptions> nativeOptions, TimeProvider? timeProvider = null) : IRecoveryPrecheckService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public bool Enabled => identityOptions.Value.Enabled || selectionOptions.Value.Enabled;
    public bool IdentityInputEnabled => identityOptions.Value.Enabled;
    public string LabelResourceKey => identityOptions.Value.LabelResourceKey;
    public string HelpResourceKey => identityOptions.Value.HelpResourceKey;

    public async Task<RecoveryPrepareResult> PrepareAsync(RecoveryPrepareRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (!Enabled || !ValidContext(request.Context) || string.IsNullOrWhiteSpace(request.Identifier) ||
                request.Identifier.Length > 320 || !await TakePublicBudgetAsync("prepare", request.Context, request.SourceIp, cancellationToken))
                return new();
            var name = normalizer.NormalizeName(request.Identifier.Trim());
            var email = normalizer.NormalizeEmail(request.Identifier.Trim());
            var users = await db.Users.AsNoTracking().Where(user => user.NormalizedUserName == name || user.NormalizedEmail == email)
                .Take(2).ToListAsync(cancellationToken);
            if (users.Count != 1 || !await throttle.TakeAsync("prepare", "subject", users[0].Id.ToString("D"),
                5, TimeSpan.FromMinutes(15), cancellationToken)) return new();
            var before = await ResolveStateAsync(users[0].Id, cancellationToken);
            if (before is null) return new();
            if (before.IdentityRequired)
            {
                if (before.Binding is null || !RecoveryIdentityVerificationContract.IsText(request.IdentityIdentifier, 128, true)) return new();
                var wire = new RecoveryIdentityVerificationRequest
                {
                    RequestId = Guid.NewGuid().ToString("D"), ProviderNamespace = before.Binding.ProviderNamespace,
                    StableSubject = before.Binding.StableSubject,
                    Evidence = new() { IdentityIdentifier = request.IdentityIdentifier }
                };
                var response = await verifier.VerifyAsync(wire, cancellationToken);
                if (response is null || !response.IsValid() || response.Outcome != RecoveryIdentityVerificationOutcome.Verified ||
                    response.RequestId != wire.RequestId || response.Binding?.ProviderNamespace != wire.ProviderNamespace ||
                    response.Binding.StableSubject != wire.StableSubject || response.Binding.Scheme != wire.Scheme) return new();
            }
            // Fresh detached reads after the external call; provider success cannot override local changes.
            var after = await ResolveStateAsync(before.User.Id, cancellationToken);
            if (after is null || before.SecurityDigest != after.SecurityDigest || !before.Destination.Matches(after.Destination)) return new();
            var now = _time.GetUtcNow();
            var lifetime = Math.Min(Math.Clamp(identityOptions.Value.PrecheckLifetimeMinutes, 1, 5), nativeOptions.Value.NativeOtpLifetimeMinutes);
            var grant = new RecoveryPrecheckGrant(after.User.Id, after.Binding?.Id, after.BindingDigest,
                after.User.SecurityStamp!, request.Context.ContextHash, request.Context.CsrfHash, after.PolicyDigest,
                after.Destination.SelectionEpoch, after.Destination.Kind, after.Destination.Fingerprint,
                after.Destination.Version, now, now.AddMinutes(lifetime));
            db.RecoveryPrecheckGrants.Add(grant);
            await db.SaveChangesAsync(cancellationToken);
            return new(grant.Id, after.Destination.MaskedAddress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(); }
        finally
        {
            // Bounded floor for cheap failure paths. Provider latency remains bounded by its own deadline.
            var remaining = TimeSpan.FromMilliseconds(150) - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
        }
    }

    public async Task<NativeRecoveryStartResult> SendOtpAsync(RecoverySendOtpRequest request, CancellationToken cancellationToken = default)
    {
        var denied = new NativeRecoveryStartResult(Guid.NewGuid());
        try
        {
            if (!Enabled || !ValidContext(request.Context) ||
                !await TakePublicBudgetAsync("send", request.Context, request.SourceIp, cancellationToken)) return denied;
            RecoveryProofChallenge challenge;
            RecoveryCeremonyState state;
            string code;
            await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken))
            {
                var grant = await db.RecoveryPrecheckGrants.SingleOrDefaultAsync(g => g.Id == request.GrantId, cancellationToken);
                var current = grant is null ? null : await ResolveStateAsync(grant.LocalAccountId, cancellationToken);
                if (grant is null || current is null || !Matches(grant, current, request.Context) ||
                    grant.ConsumedAtUtc is not null || grant.RevokedAtUtc is not null || grant.ExpiresAtUtc <= _time.GetUtcNow()) return denied;
                state = current;
                var now = _time.GetUtcNow();
                // The shared subject CAS also serializes default recipients without a permanent default row.
                if (!await throttle.TakeAsync("otp-cooldown", "subject", grant.LocalAccountId.ToString("D"), 1,
                    TimeSpan.FromSeconds(nativeOptions.Value.NativeOtpResendCooldownSeconds), cancellationToken)) return denied;
                RecoveryEmailRecord? active = null;
                if (state.Destination.RecoveryEmailId is { } emailId)
                {
                    active = await db.RecoveryEmails.SingleOrDefaultAsync(e => e.Id == emailId, cancellationToken);
                    if (active is null || !active.TryReserveSend(now, now.AddSeconds(nativeOptions.Value.NativeOtpResendCooldownSeconds))) return denied;
                }
                code = RecoveryProofSecurity.GenerateNumericCode();
                var hash = hasher.HashPassword(state.User, RecoveryProofSecurity.BindToContext(code,
                    request.Context.ContextHash, request.Context.CsrfHash, OtpBinding(state.Destination)));
                challenge = active is null
                    ? RecoveryProofChallenge.CreateForDefault(state.User.Id, hash, now, now.AddMinutes(nativeOptions.Value.NativeOtpLifetimeMinutes),
                        state.Destination.SelectionEpoch, state.Destination.Fingerprint, state.Destination.Version)
                    : new RecoveryProofChallenge(active.Id, state.User.Id, RecoveryProofPurpose.NativePasswordRecovery,
                        hash, now, now.AddMinutes(nativeOptions.Value.NativeOtpLifetimeMinutes));
                if (active is not null) challenge.BindSelection(state.Destination.SelectionEpoch, state.Destination.Kind,
                    state.Destination.Fingerprint, state.Destination.Version);
                challenge.BindNativeAssistance(request.Context.ContextHash, request.Context.CsrfHash, state.IsDirectory,
                    state.IsDirectory ? state.Binding!.DirectoryObjectId : null, active?.Version ?? state.Destination.Version, state.User.SecurityStamp!);
                if (!grant.TryReserveChallenge(challenge, now)) return denied;
                var previous = await db.RecoveryProofChallenges.Where(c => c.LocalAccountId == state.User.Id &&
                    c.Purpose == RecoveryProofPurpose.NativePasswordRecovery && c.RevokedAtUtc == null && c.ConsumedAtUtc == null)
                    .ToListAsync(cancellationToken);
                foreach (var old in previous) old.Revoke(now);
                db.RecoveryProofChallenges.Add(challenge);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            // SMTP is outside the reservation transaction. A crash/cancellation leaves Reserved unredeemable.
            var sent = await delivery.SendAsync(state.Destination.Address, code, nativeOptions.Value.NativeOtpLifetimeMinutes, cancellationToken);
            if (!challenge.TryCompleteDelivery(_time.GetUtcNow(), sent)) return denied;
            await db.SaveChangesAsync(cancellationToken);
            return sent ? new(challenge.Id) : denied;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return denied; }
    }

    // Shared with later verify/reset integration; the linked consumed grant remains authority evidence, never OTP proof.
    internal async Task<RecoveryCeremonyState?> ResolveStateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var native = nativeOptions.Value;
        if (!native.NativeRecoveryEnabled || native.DeploymentCeiling != ForgotPasswordMode.Native) return null;
        var policy = await db.SecurityPolicies.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (routing.Evaluate(policy?.ForgotPasswordMode ?? ForgotPasswordMode.Disabled, policy?.CustomForgotPasswordUrl).PermittedMode != ForgotPasswordMode.Native) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        if (user is null || !user.IsActive || user.IsDeleted || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            user.LockoutEnabled && user.LockoutEnd > _time.GetUtcNow()) return null;
        if (user.PersonId is { } personId)
        {
            var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
            if (person?.CanAuthenticate() != true) return null;
        }
        if (await db.NativeDirectoryRecoveryAttempts.AsNoTracking().AnyAsync(a => a.LocalAccountId == accountId &&
            (a.Status == NativeDirectoryRecoveryStatus.Reserved || a.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired), cancellationToken)) return null;
        var binding = await db.ProviderSubjectDirectoryBindings.AsNoTracking().SingleOrDefaultAsync(b => b.LocalAccountId == accountId, cancellationToken);
        var migration = await db.CredentialMigrationStateRecords.AsNoTracking().SingleOrDefaultAsync(m => m.LocalAccountId == accountId, cancellationToken);
        var directory = binding is not null || migration is not null;
        var identity = identityOptions.Value;
        var required = identity.Enabled && (directory ? identity.RequireForDirectoryAccounts : identity.RequireForLocalAccounts);
        if (required && binding is null) return null;
        if (directory ? !native.NativeDirectoryRecoveryEnabled || binding is null || migration is null ||
            migration.State != CredentialMigrationState.LocalFinalized || migration.ProviderSubjectDirectoryBindingId != binding.Id
            : string.IsNullOrWhiteSpace(user.PasswordHash)) return null;
        var destination = (await resolver.ResolveAsync(accountId, cancellationToken)).Destination;
        if (destination is null) return null;
        var bindingDigest = binding is null ? null : BindingDigest(binding);
        var policyDigest = RecoveryDestinationBinding.ComputeDigest("recovery-ceremony-policy-v1", destination.EffectivePolicyDigest,
            identity.Enabled.ToString(), identity.RequireForLocalAccounts.ToString(), identity.RequireForDirectoryAccounts.ToString(),
            identity.Scheme, identity.Endpoint, identity.PrecheckLifetimeMinutes.ToString(CultureInfo.InvariantCulture),
            native.NativeRecoveryEnabled.ToString(), native.NativeDirectoryRecoveryEnabled.ToString(), native.DeploymentCeiling.ToString(),
            policy?.ForgotPasswordMode.ToString(), native.NativeOtpLifetimeMinutes.ToString(CultureInfo.InvariantCulture),
            native.NativeOtpMaxAttempts.ToString(CultureInfo.InvariantCulture), native.NativeOtpResendCooldownSeconds.ToString(CultureInfo.InvariantCulture));
        var securityDigest = RecoveryDestinationBinding.ComputeDigest("recovery-account-v1", accountId.ToString("D"), user.SecurityStamp,
            bindingDigest, directory.ToString(), policyDigest);
        return new(user, binding, directory, required, destination, bindingDigest, policyDigest, securityDigest);
    }

    internal static string BindingDigest(ProviderSubjectDirectoryBinding binding) => RecoveryDestinationBinding.ComputeDigest(
        "recovery-provider-binding-v1", binding.Id.ToString("D"), binding.LocalAccountId.ToString("D"),
        binding.ProviderNamespace, binding.StableSubject, binding.DirectoryObjectId.ToString("D"));

    internal static string OtpBinding(RecoveryDestination destination) => RecoveryDestinationBinding.ComputeDigest(
        "recovery-otp-destination-v1", destination.LocalAccountId.ToString("D"), destination.Kind.ToString(), destination.Fingerprint,
        destination.Version.ToString(CultureInfo.InvariantCulture), destination.SelectionEpoch.ToString(CultureInfo.InvariantCulture));

    internal static bool Matches(RecoveryPrecheckGrant grant, RecoveryCeremonyState state, NativeRecoveryContext context) =>
        grant.LocalAccountId == state.User.Id && grant.SecurityStamp == state.User.SecurityStamp &&
        grant.ProviderBindingId == state.Binding?.Id && grant.ProviderBindingVersion == state.BindingDigest &&
        grant.EffectivePolicyVersion == state.PolicyDigest && grant.ContextHash == context.ContextHash && grant.CsrfHash == context.CsrfHash &&
        grant.SelectionEpoch == state.Destination.SelectionEpoch && grant.DestinationKind == state.Destination.Kind &&
        grant.DestinationFingerprint == state.Destination.Fingerprint && grant.DestinationVersion == state.Destination.Version;

    private async Task<bool> TakePublicBudgetAsync(string operation, NativeRecoveryContext context, string ip, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(ip) && ip.Length <= 64 &&
        await throttle.TakeAsync(operation, "ip", ip, 20, TimeSpan.FromMinutes(15), ct) &&
        await throttle.TakeAsync(operation, "browser", context.BrowserHash ?? context.ContextHash, 5, TimeSpan.FromMinutes(15), ct);

    private static bool ValidContext(NativeRecoveryContext context) =>
        !string.IsNullOrWhiteSpace(context.ContextHash) && context.ContextHash.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.CsrfHash) && context.CsrfHash.Length <= 256;
}

internal sealed record RecoveryCeremonyState(ApplicationUser User, ProviderSubjectDirectoryBinding? Binding,
    bool IsDirectory, bool IdentityRequired, RecoveryDestination Destination, string? BindingDigest, string PolicyDigest, string SecurityDigest);
