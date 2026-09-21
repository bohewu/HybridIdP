using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Recovery is callable only from an authenticated request authorized to update users.
/// </summary>
public sealed class HttpContextMigrationRecoveryAuthorizer : IMigrationRecoveryAuthorizer
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public HttpContextMigrationRecoveryAuthorizer(
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public async Task<bool> IsAuthorizedAsync(CancellationToken cancellationToken = default)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return (await _authorizationService.AuthorizeAsync(user, null, Permissions.Users.Update)).Succeeded;
    }
}

/// <summary>
/// Independently verifies directory commitment or current local finalization prerequisites.
/// It never persists, logs, or derives a submitted password.
/// </summary>
public sealed class MigrationRecoveryEvidenceReader : IMigrationRecoveryEvidenceReader
{
    private readonly IDirectoryCredentialVerifier _directoryVerifier;
    private readonly IApplicationDbContext _dbContext;

    public MigrationRecoveryEvidenceReader(
        IDirectoryCredentialVerifier directoryVerifier,
        IApplicationDbContext dbContext)
    {
        _directoryVerifier = directoryVerifier;
        _dbContext = dbContext;
    }

    public async Task<MigrationCommittedEvidence> ReadDirectoryCommitmentAsync(
        CredentialMigrationRecord record,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (record.State != CredentialMigrationState.ProofValidated ||
            !CredentialMigrationStateRecord.IsPersistableRequirement(record.EffectiveEmailOtpRequirement) ||
            string.IsNullOrEmpty(newPassword))
        {
            return MigrationCommittedEvidence.None;
        }

        try
        {
            var verification = await _directoryVerifier.VerifyCredentialAsync(
                record.Binding.DirectoryObjectId,
                newPassword,
                cancellationToken);
            return verification.Outcome == DirectoryCredentialOutcome.Authenticated &&
                   verification.Identity is
                   {
                       ObjectId: var objectId,
                       IsEligible: true,
                       IsEnabled: true,
                       IsLocked: false
                   } && objectId == record.Binding.DirectoryObjectId
                ? MigrationCommittedEvidence.DirectoryCredentialCommitted
                : MigrationCommittedEvidence.None;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return MigrationCommittedEvidence.None;
        }
    }

    public async Task<MigrationCommittedEvidence> ReadLocalFinalizationAsync(
        CredentialMigrationRecord record,
        CancellationToken cancellationToken = default)
    {
        if (record.State is not (CredentialMigrationState.DirectoryCredentialCommitted or CredentialMigrationState.LocalFinalized))
        {
            return MigrationCommittedEvidence.None;
        }

        if (!CredentialMigrationStateRecord.IsPersistableRequirement(record.EffectiveEmailOtpRequirement))
        {
            return MigrationCommittedEvidence.None;
        }

        try
        {
            var binding = await _dbContext.ProviderSubjectDirectoryBindings
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.LocalAccountId == record.LocalAccountId &&
                    candidate.ProviderNamespace == record.Binding.ProviderNamespace &&
                    candidate.StableSubject == record.Binding.StableSubject &&
                    candidate.DirectoryObjectId == record.Binding.DirectoryObjectId,
                    cancellationToken);
            var user = await _dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == record.LocalAccountId, cancellationToken);
            if (binding is null || user is null || !user.IsActive || user.IsDeleted)
            {
                return MigrationCommittedEvidence.None;
            }

            Person? person = null;
            if (user.PersonId is { } personId)
            {
                person = await _dbContext.Persons
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken);
                if (person?.CanAuthenticate() != true)
                {
                    return MigrationCommittedEvidence.None;
                }
            }

            if (record.EffectiveEmailOtpRequirement == EffectiveEmailOtpRequirement.Required &&
                !HasCurrentEmailOtpEvidence(user, person))
            {
                return MigrationCommittedEvidence.None;
            }

            return MigrationCommittedEvidence.LocalFinalized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return MigrationCommittedEvidence.None;
        }
    }

    private static bool HasCurrentEmailOtpEvidence(ApplicationUser user, Person? person) =>
        user.EmailMfaEnabled &&
        !string.IsNullOrWhiteSpace(user.Email) &&
        (person is null ||
         (!string.IsNullOrWhiteSpace(person.Email) &&
          string.Equals(user.Email, person.Email, StringComparison.OrdinalIgnoreCase)));
}
