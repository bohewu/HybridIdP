using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>
/// Creates immutable bindings only for a unique eligible directory result and refreshes local display fields.
/// </summary>
public sealed class Stage1BindingRefreshService : IStage1BindingRefreshService
{
    private readonly IApplicationDbContext _dbContext;

    private readonly INativeFieldPolicyService _nativeFields;

    public Stage1BindingRefreshService(IApplicationDbContext dbContext, INativeFieldPolicyService nativeFields)
    {
        _dbContext = dbContext;
        _nativeFields = nativeFields;
    }

    public async Task<Stage1BindingRefreshOutcome> BindAndRefreshAsync(
        Stage1BindingRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(request))
        {
            return Stage1BindingRefreshOutcome.Invalid;
        }

        var subjectBinding = await _dbContext.ProviderSubjectDirectoryBindings
            .SingleOrDefaultAsync(binding =>
                binding.ProviderNamespace == request.ProviderNamespace &&
                binding.StableSubject == request.StableSubject,
                cancellationToken);
        var objectBinding = await _dbContext.ProviderSubjectDirectoryBindings
            .SingleOrDefaultAsync(binding => binding.DirectoryObjectId == request.DirectoryIdentity.ObjectId, cancellationToken);
        var accountBinding = await _dbContext.ProviderSubjectDirectoryBindings
            .SingleOrDefaultAsync(binding => binding.LocalAccountId == request.LocalAccountId, cancellationToken);
        var normalizedAlias = ProviderSubjectDirectoryBinding.NormalizeCanonicalAccountAlias(
            request.DirectoryIdentity.CanonicalAccount);
        var aliasBindings = normalizedAlias is null
            ? []
            : await _dbContext.ProviderSubjectDirectoryBindings
                .Where(binding => binding.NormalizedCanonicalAccountAlias == normalizedAlias)
                .ToListAsync(cancellationToken);
        var aliasBinding = aliasBindings.Count == 1 ? aliasBindings[0] : null;

        if (!MatchesOrAbsent(subjectBinding, request) ||
            !MatchesOrAbsent(objectBinding, request) ||
            !MatchesOrAbsent(accountBinding, request) ||
            aliasBindings.Count > 1 ||
            !MatchesOrAbsent(aliasBinding, request))
        {
            return Stage1BindingRefreshOutcome.Conflict;
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == request.LocalAccountId,
            cancellationToken);
        if (user is null)
        {
            return Stage1BindingRefreshOutcome.Invalid;
        }

        var person = user.PersonId is { } personId
            ? await _dbContext.Persons.SingleOrDefaultAsync(candidate => candidate.Id == personId, cancellationToken)
            : null;

        var existingBinding = subjectBinding ?? objectBinding ?? accountBinding ?? aliasBinding;
        var binding = existingBinding ?? new ProviderSubjectDirectoryBinding(
            request.LocalAccountId, request.ProviderNamespace, request.StableSubject,
            request.DirectoryIdentity.ObjectId, DateTime.UtcNow, request.DirectoryIdentity.CanonicalAccount);
        await using var transaction = _dbContext is DbContext db && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            if (existingBinding is null)
                await _dbContext.ProviderSubjectDirectoryBindings.AddAsync(binding, cancellationToken);
            else
                binding.SetCanonicalAccountAlias(request.DirectoryIdentity.CanonicalAccount);

            // Source policy checks persisted exact bindings, never pending inferred links.
            await _dbContext.SaveChangesAsync(cancellationToken);
            RefreshContactProfile(user, person, request.DirectoryIdentity.Profile);
            if (request.DirectoryIdentity.Profile is { } profile)
            {
                await _nativeFields.ApplySourceAsync(user, person,
                    new(NativeFieldSource.Stage1, user.Id, request.ProviderNamespace,
                        request.StableSubject, request.DirectoryIdentity.ObjectId),
                    new Dictionary<NativeField, string?>
                    {
                        [NativeField.Nickname] = profile.DisplayName, [NativeField.FirstName] = profile.GivenName,
                        [NativeField.LastName] = profile.Surname, [NativeField.Department] = profile.Department,
                        [NativeField.JobTitle] = profile.Title, [NativeField.EmployeeId] = profile.EmployeeId
                    }, cancellationToken);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            DetachNativePolicyGuards();
            _dbContext.Detach(user);
            if (person is not null) _dbContext.Detach(person);
            _dbContext.Detach(binding);
            return Stage1BindingRefreshOutcome.Conflict;
        }
        catch
        {
            DetachNativePolicyGuards();
            _dbContext.Detach(user);
            if (person is not null) _dbContext.Detach(person);
            _dbContext.Detach(binding);
            throw;
        }

        return existingBinding is null
            ? Stage1BindingRefreshOutcome.BoundAndRefreshed
            : Stage1BindingRefreshOutcome.ExistingBindingRefreshed;
    }

    private static bool IsValid(Stage1BindingRefreshRequest request) =>
        request.LocalAccountId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(request.ProviderNamespace) &&
        !string.IsNullOrWhiteSpace(request.StableSubject) &&
        request.DirectoryIdentity.ObjectId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(request.DirectoryIdentity.CanonicalAccount) &&
        request.DirectoryIdentity.IsEligible &&
        request.DirectoryIdentity.IsEnabled &&
        !request.DirectoryIdentity.IsLocked &&
        (request.DirectoryIdentity.Profile is null || request.DirectoryIdentity.Profile.IsValid());

    private void DetachNativePolicyGuards()
    {
        if (_dbContext is DbContext db)
            foreach (var entry in db.ChangeTracker.Entries<NativeFieldSourcePolicy>().ToArray())
                entry.State = EntityState.Detached;
    }

    private static bool MatchesOrAbsent(
        ProviderSubjectDirectoryBinding? binding,
        Stage1BindingRefreshRequest request) =>
        binding is null ||
        (binding.Matches(request.LocalAccountId, request.DirectoryIdentity.ObjectId) &&
         binding.ProviderNamespace == request.ProviderNamespace &&
         binding.StableSubject == request.StableSubject);

    private static void RefreshContactProfile(
        ApplicationUser user,
        Person? person,
        AssuredProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        Apply(profile.Email, value =>
        {
            // Upstream profile assurance cannot replace an enrolled local factor.
            if (!user.EmailMfaEnabled && !string.Equals(user.Email, value, StringComparison.Ordinal))
            {
                user.EmailMfaCode = null;
                user.EmailMfaCodeExpiry = null;
                user.EmailMfaVerificationAttempts = 0;
                user.Email = value;
            }
        }, value =>
        {
            if (person is not null)
            {
                person.Email = value;
            }
        });
        user.ModifiedAt = DateTime.UtcNow;
        if (person is not null)
        {
            person.ModifiedAt = DateTime.UtcNow;
        }
    }

    private static void Apply(string? value, Action<string> updateUser, Action<string> updatePerson)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        updateUser(value);
        updatePerson(value);
    }
}
