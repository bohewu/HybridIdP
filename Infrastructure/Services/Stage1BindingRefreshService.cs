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

    public Stage1BindingRefreshService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
        if (existingBinding is null)
        {
            await _dbContext.ProviderSubjectDirectoryBindings.AddAsync(
                new ProviderSubjectDirectoryBinding(
                    request.LocalAccountId,
                    request.ProviderNamespace,
                    request.StableSubject,
                    request.DirectoryIdentity.ObjectId,
                    DateTime.UtcNow,
                    request.DirectoryIdentity.CanonicalAccount),
                cancellationToken);
        }
        else
        {
            existingBinding.SetCanonicalAccountAlias(request.DirectoryIdentity.CanonicalAccount);
        }

        RefreshLocalProfile(user, person, request.DirectoryIdentity.Profile);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Stage1BindingRefreshOutcome.Conflict;
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

    private static bool MatchesOrAbsent(
        ProviderSubjectDirectoryBinding? binding,
        Stage1BindingRefreshRequest request) =>
        binding is null ||
        (binding.Matches(request.LocalAccountId, request.DirectoryIdentity.ObjectId) &&
         binding.ProviderNamespace == request.ProviderNamespace &&
         binding.StableSubject == request.StableSubject);

    private static void RefreshLocalProfile(
        ApplicationUser user,
        Person? person,
        AssuredProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        Apply(profile.DisplayName, value => user.Nickname = value, value =>
        {
            if (person is not null)
            {
                person.Nickname = value;
            }
        });
        Apply(profile.GivenName, value => user.FirstName = value, value =>
        {
            if (person is not null)
            {
                person.FirstName = value;
            }
        });
        Apply(profile.Surname, value => user.LastName = value, value =>
        {
            if (person is not null)
            {
                person.LastName = value;
            }
        });
        Apply(profile.Email, value => user.Email = value, value =>
        {
            if (person is not null)
            {
                person.Email = value;
            }
        });
        Apply(profile.Department, value => user.Department = value, value =>
        {
            if (person is not null)
            {
                person.Department = value;
            }
        });
        Apply(profile.Title, value => user.JobTitle = value, value =>
        {
            if (person is not null)
            {
                person.JobTitle = value;
            }
        });
        Apply(profile.EmployeeId, value => user.EmployeeId = value, value =>
        {
            if (person is not null)
            {
                person.EmployeeId = value;
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
