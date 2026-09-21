using System.Security.Cryptography;
using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public sealed class AdminTemporaryCredentialService : IAdminTemporaryCredentialService
{
    private const string RevocationReason = "admin-temporary-credential";
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*-_+";

    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly IRecoveryProofAuthorizer _authorizer;
    private readonly IRecoveryProofAudit _audit;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ForgotPasswordRecoveryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IDirectoryTemporaryCredentialCapability? _directoryCapability;

    public AdminTemporaryCredentialService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictTokenManager tokenManager,
        IRecoveryProofAuthorizer authorizer,
        IRecoveryProofAudit audit,
        ISecurityPolicyService securityPolicyService,
        IOptions<ForgotPasswordRecoveryOptions> options,
        TimeProvider? timeProvider = null,
        IDirectoryTemporaryCredentialCapability? directoryCapability = null)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _authorizationManager = authorizationManager;
        _tokenManager = tokenManager;
        _authorizer = authorizer;
        _audit = audit;
        _securityPolicyService = securityPolicyService;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _directoryCapability = directoryCapability;
    }

    public async Task<AdminTemporaryCredentialResult> IssueAsync(
        AdminTemporaryCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.AdminTemporaryCredentialsEnabled ||
            request.ActorAccountId == Guid.Empty || request.TargetAccountId == Guid.Empty ||
            !HasRequiredEvidence(request.IdentityCheckEvidence, request.Reason))
        {
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }

        if (!await _authorizer.IsAdministratorAuthorizedAsync(request.ActorAccountId, cancellationToken))
        {
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unauthorized);
        }

        var migration = await (
            from state in _dbContext.CredentialMigrationStateRecords.AsNoTracking()
            join binding in _dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
                on state.ProviderSubjectDirectoryBindingId equals binding.Id
            join user in _dbContext.Users.AsNoTracking()
                on state.LocalAccountId equals user.Id
            where state.LocalAccountId == request.TargetAccountId
                && binding.LocalAccountId == request.TargetAccountId
            select new { state.State, binding.DirectoryObjectId, user.IsActive, user.IsDeleted }).SingleOrDefaultAsync(cancellationToken);
        if (migration is not null)
        {
            return migration.State == CredentialMigrationState.LocalFinalized && migration.IsActive &&
                   !migration.IsDeleted && _directoryCapability is not null
                ? await IssueDirectoryAsync(request, migration.DirectoryObjectId, cancellationToken)
                : new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }

        if (await _dbContext.ProviderSubjectDirectoryBindings.AsNoTracking().AnyAsync(
                binding => binding.LocalAccountId == request.TargetAccountId, cancellationToken))
        {
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.SingleOrDefaultAsync(
                candidate => candidate.Id == request.TargetAccountId,
                cancellationToken);
            if (user is null || !user.IsActive || user.IsDeleted || string.IsNullOrWhiteSpace(user.PasswordHash) ||
                await _dbContext.ProviderSubjectDirectoryBindings.AnyAsync(
                    binding => binding.LocalAccountId == request.TargetAccountId,
                    cancellationToken) ||
                await _dbContext.CredentialMigrationStateRecords.AnyAsync(
                    state => state.LocalAccountId == request.TargetAccountId,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
            }

            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            var temporaryPassword = GeneratePassword(Math.Max(20, policy.MinPasswordLength));
            var previousHash = user.PasswordHash;
            var previousChangeDate = user.LastPasswordChangeDate;
            user.RequiresPasswordChange = true;
            user.LastPasswordChangeDate = null;
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await _userManager.ResetPasswordAsync(user, resetToken, temporaryPassword);
            if (!reset.Succeeded || string.IsNullOrWhiteSpace(user.PasswordHash) ||
                string.Equals(user.PasswordHash, previousHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
            }

            user.PasswordHistory = AddPasswordHistory(user.PasswordHistory, previousHash, policy.PasswordHistoryCount);
            user.LastPasswordChangeDate = _timeProvider.GetUtcNow().UtcDateTime;
            user.RequiresPasswordChange = true;
            await RevokeSessionsAndTokensAsync(user, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(
                new RecoveryProofAuditEvent(
                    Guid.NewGuid(),
                    RecoveryProofAuditCategory.AdminTemporaryCredentialIssued,
                    user.Id,
                    request.ActorAccountId),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Success, temporaryPassword);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }
    }

    private async Task<AdminTemporaryCredentialResult> IssueDirectoryAsync(
        AdminTemporaryCredentialRequest request,
        Guid directoryObjectId,
        CancellationToken cancellationToken)
    {
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        var temporaryPassword = GeneratePassword(Math.Max(20, policy.MinPasswordLength));
        var attempt = new NativeDirectoryRecoveryAttempt(
            request.TargetAccountId,
            directoryObjectId,
            NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
            _timeProvider.GetUtcNow());

        try
        {
            await using var reservation = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            _dbContext.NativeDirectoryRecoveryAttempts.Add(attempt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await reservation.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }

        DirectoryCredentialOperationResult operation;
        try
        {
            operation = await _directoryCapability!.IssueTemporaryCredentialAsync(
                directoryObjectId, temporaryPassword, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompleteAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
            throw;
        }
        catch
        {
            await CompleteAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }

        if (operation.Outcome is DirectoryCredentialOperationOutcome.Unavailable or DirectoryCredentialOperationOutcome.Timeout)
        {
            await CompleteAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                NativeDirectoryRecoveryStatus.ReconciliationRequired, cancellationToken);
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }
        if (operation.Outcome != DirectoryCredentialOperationOutcome.Succeeded)
        {
            await CompleteAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                NativeDirectoryRecoveryStatus.Denied, cancellationToken);
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }

        try
        {
            await using var finalization = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var currentAttempt = await FindAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue, cancellationToken);
            var user = await _dbContext.Users.SingleAsync(
                candidate => candidate.Id == request.TargetAccountId, cancellationToken);
            var validBinding = await (
                from state in _dbContext.CredentialMigrationStateRecords
                join binding in _dbContext.ProviderSubjectDirectoryBindings
                    on state.ProviderSubjectDirectoryBindingId equals binding.Id
                where state.LocalAccountId == request.TargetAccountId &&
                      state.State == CredentialMigrationState.LocalFinalized &&
                      binding.LocalAccountId == request.TargetAccountId &&
                      binding.DirectoryObjectId == directoryObjectId
                select state.Id).AnyAsync(cancellationToken);
            if (!validBinding || !user.IsActive || user.IsDeleted)
            {
                throw new InvalidOperationException("Directory credential authority changed before finalization.");
            }

            user.RequiresPasswordChange = true;
            await _userManager.UpdateSecurityStampAsync(user);
            await RevokeSessionsAndTokensAsync(user, cancellationToken);
            currentAttempt.Complete(NativeDirectoryRecoveryStatus.Succeeded, _timeProvider.GetUtcNow());
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _audit.RecordAsync(new RecoveryProofAuditEvent(
                Guid.NewGuid(), RecoveryProofAuditCategory.AdminTemporaryCredentialIssued,
                user.Id, request.ActorAccountId), cancellationToken);
            await finalization.CommitAsync(cancellationToken);
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Success, temporaryPassword);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _dbContext.ChangeTracker.Clear();
            await CompleteAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
            throw;
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            await CompleteAttemptAsync(attempt.Id, directoryObjectId,
                NativeDirectoryCredentialOperationKind.AdminTemporaryIssue,
                NativeDirectoryRecoveryStatus.ReconciliationRequired, CancellationToken.None);
            return new AdminTemporaryCredentialResult(RecoveryProofOutcome.Unavailable);
        }
    }

    private async Task CompleteAttemptAsync(
        Guid attemptId,
        Guid directoryObjectId,
        NativeDirectoryCredentialOperationKind operationKind,
        NativeDirectoryRecoveryStatus status,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var attempt = await FindAttemptAsync(attemptId, directoryObjectId, operationKind, cancellationToken);
        attempt.Complete(status, _timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<NativeDirectoryRecoveryAttempt> FindAttemptAsync(
        Guid attemptId,
        Guid directoryObjectId,
        NativeDirectoryCredentialOperationKind operationKind,
        CancellationToken cancellationToken) =>
        _dbContext.NativeDirectoryRecoveryAttempts.SingleAsync(candidate =>
            candidate.Id == attemptId &&
            candidate.DirectoryObjectId == directoryObjectId &&
            candidate.OperationKind == operationKind &&
            (candidate.Status == NativeDirectoryRecoveryStatus.Reserved ||
             candidate.Status == NativeDirectoryRecoveryStatus.ReconciliationRequired),
            cancellationToken);

    private async Task RevokeSessionsAndTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var sessions = await _dbContext.UserSessions
            .Where(session => session.UserId == user.Id && session.RevokedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedUtc = now;
            session.RevocationReason = RevocationReason;
        }

        var authorizations = new List<object>();
        await foreach (var authorization in _authorizationManager.FindBySubjectAsync(user.Id.ToString(), cancellationToken))
        {
            authorizations.Add(authorization);
        }
        foreach (var authorization in authorizations)
        {
            if (!await _authorizationManager.TryRevokeAsync(authorization, cancellationToken))
            {
                throw new InvalidOperationException("An OpenIddict authorization could not be revoked.");
            }
        }

        var tokens = new Dictionary<string, object>(StringComparer.Ordinal);
        await foreach (var token in _tokenManager.FindBySubjectAsync(user.Id.ToString(), cancellationToken))
        {
            var id = await _tokenManager.GetIdAsync(token, cancellationToken);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("An OpenIddict token has no identifier.");
            tokens.TryAdd(id, token);
        }
        foreach (var authorization in authorizations)
        {
            var authorizationId = await _authorizationManager.GetIdAsync(authorization, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorizationId)) throw new InvalidOperationException("An OpenIddict authorization has no identifier.");
            await foreach (var token in _tokenManager.FindByAuthorizationIdAsync(authorizationId, cancellationToken))
            {
                var id = await _tokenManager.GetIdAsync(token, cancellationToken);
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("An OpenIddict token has no identifier.");
                tokens.TryAdd(id, token);
            }
        }
        foreach (var token in tokens.Values)
        {
            if (!await _tokenManager.TryRevokeAsync(token, cancellationToken))
            {
                throw new InvalidOperationException("An OpenIddict token could not be revoked.");
            }
        }
    }

    private static string GeneratePassword(int length)
    {
        var all = Uppercase + Lowercase + Digits + Symbols;
        var characters = new char[length];
        characters[0] = Uppercase[RandomNumberGenerator.GetInt32(Uppercase.Length)];
        characters[1] = Lowercase[RandomNumberGenerator.GetInt32(Lowercase.Length)];
        characters[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        characters[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];
        for (var index = 4; index < characters.Length; index++)
        {
            characters[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }
        for (var index = characters.Length - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swap]) = (characters[swap], characters[index]);
        }
        return new string(characters);
    }

    private static bool HasRequiredEvidence(string evidence, string reason) =>
        !string.IsNullOrWhiteSpace(evidence) && evidence.Trim().Length <= 500 &&
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;

    private static string AddPasswordHistory(string serializedHistory, string previousHash, int count)
    {
        List<string> history;
        try { history = JsonSerializer.Deserialize<List<string>>(serializedHistory) ?? []; }
        catch (JsonException) { history = []; }
        history.Insert(0, previousHash);
        return JsonSerializer.Serialize(history.Distinct(StringComparer.Ordinal).Take(Math.Max(0, count)).ToList());
    }
}
