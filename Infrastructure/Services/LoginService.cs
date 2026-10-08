using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service for authenticating users via local or legacy systems.
/// Phase 18: Added Person lifecycle validation to block login for inactive persons.
/// </summary>
public partial class LoginService : ILoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<LoginService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Core.Application.Options.ExternalLoginOptions _externalLoginOptions;
    private readonly IProofProvider? _proofProvider;
    private readonly IDirectoryIdentityLookup? _directoryIdentityLookup;
    private readonly IStage1BindingRefreshService? _stage1BindingRefreshService;
    private readonly IStage2CredentialMigrationService? _stage2CredentialMigrationService;
    private readonly ICredentialMigrationStateStore? _credentialMigrationStateStore;
    private readonly IProviderMetadataRefreshService? _providerMetadataRefreshService;
    private readonly DirectoryIntegrationOptions _directoryIntegrationOptions;
    private readonly CredentialMigrationOptions _credentialMigrationOptions;
    private readonly ProviderProofOptions _proofOptions;
    private readonly IProviderProfileService? _providerProfileService;

    public LoginService(
        UserManager<ApplicationUser> userManager,
        ISecurityPolicyService securityPolicyService,
        IJitProvisioningService jitProvisioningService,
        IApplicationDbContext dbContext,
        ILogger<LoginService> logger,
        Microsoft.Extensions.Options.IOptions<Core.Application.Options.ExternalLoginOptions> externalLoginOptions,
        TimeProvider? timeProvider = null,
        IProofProvider? proofProvider = null,
        IDirectoryIdentityLookup? directoryIdentityLookup = null,
        IStage1BindingRefreshService? stage1BindingRefreshService = null,
        Microsoft.Extensions.Options.IOptions<DirectoryIntegrationOptions>? directoryIntegrationOptions = null,
        IStage2CredentialMigrationService? stage2CredentialMigrationService = null,
        Microsoft.Extensions.Options.IOptions<CredentialMigrationOptions>? credentialMigrationOptions = null,
        ICredentialMigrationStateStore? credentialMigrationStateStore = null,
        IProviderMetadataRefreshService? providerMetadataRefreshService = null,
        Microsoft.Extensions.Options.IOptions<ProviderProofOptions>? providerProofOptions = null,
        IProviderProfileService? providerProfileService = null)
    {
        _userManager = userManager;
        _securityPolicyService = securityPolicyService;
        _jitProvisioningService = jitProvisioningService;
        _dbContext = dbContext;
        _logger = logger;
        _externalLoginOptions = externalLoginOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _proofProvider = proofProvider;
        _directoryIdentityLookup = directoryIdentityLookup;
        _stage1BindingRefreshService = stage1BindingRefreshService;
        _directoryIntegrationOptions = directoryIntegrationOptions?.Value ?? new DirectoryIntegrationOptions();
        _stage2CredentialMigrationService = stage2CredentialMigrationService;
        _credentialMigrationOptions = credentialMigrationOptions?.Value ?? new CredentialMigrationOptions();
        _credentialMigrationStateStore = credentialMigrationStateStore;
        _providerMetadataRefreshService = providerMetadataRefreshService;
        _proofOptions = providerProofOptions?.Value ?? new ProviderProofOptions();
        _providerProfileService = providerProfileService;
    }

    public async Task<LoginResult> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(login) 
                   ?? await _userManager.FindByNameAsync(login);
        var aliasResolution = CanonicalAliasResolution.NotFound;
        if (user is null)
        {
            aliasResolution = await ResolveCanonicalAliasAsync(login, cancellationToken);
            if (aliasResolution.Conflict)
            {
                return LoginResult.InvalidCredentials();
            }

            user = aliasResolution.User;
        }

        if (user != null)
        {
            // A durable migration record is an authority boundary independent of all rollout switches.
            // If its store is unavailable, credential authority is uncertain and must fail closed.
            if (_credentialMigrationStateStore is null)
            {
                return LoginResult.InvalidCredentials();
            }

            var migration = await _credentialMigrationStateStore.FindAsync(user.Id, cancellationToken);
            if (migration is not null)
            {
                if (migration.State != CredentialMigrationState.LocalFinalized || !IsDirectoryAuthenticationEnabled())
                {
                    return LoginResult.InvalidCredentials();
                }

                return await AuthenticateCompletedDirectoryUserAsync(
                    user,
                    password,
                    migration.Binding,
                    cancellationToken);
            }

            if (aliasResolution.IsResolved)
            {
                return await AuthenticateWithoutLocalCredentialAuthorityAsync(
                    login,
                    password,
                    aliasResolution.User,
                    aliasResolution.Binding,
                    cancellationToken);
            }

            if (IsStage2Enabled())
            {
                return LoginResult.InvalidCredentials();
            }

            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                if (_directoryIntegrationOptions.Enabled)
                {
                    var bindings = await _dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
                        .Where(b => b.LocalAccountId == user.Id).Take(2).ToListAsync(cancellationToken);
                    if (bindings.Count == 1)
                        return await AuthenticateStage1ProviderUserAsync(login, password, user, bindings[0], cancellationToken);
                }
                var links = await _userManager.GetLoginsAsync(user);
                var selected = links.Where(l => l.LoginProvider == _proofOptions.TrustedProviderNamespace).ToList();
                if (!_proofOptions.Enabled || selected.Count != 1)
                    return LoginResult.InvalidCredentials();
                return await AuthenticateContractUserAsync(login, password, user, selected[0].ProviderKey, cancellationToken);
            }
            return await AuthenticateLocalUserAsync(user, password, cancellationToken);
        }

        return await AuthenticateWithoutLocalCredentialAuthorityAsync(
            login,
            password,
            existingAliasUser: null,
            existingAliasBinding: null,
            cancellationToken);
    }

    private async Task<LoginResult> AuthenticateWithoutLocalCredentialAuthorityAsync(
        string login,
        string password,
        ApplicationUser? existingAliasUser,
        ProviderSubjectDirectoryBinding? existingAliasBinding,
        CancellationToken cancellationToken)
    {
        if (IsStage2Enabled())
        {
            // Stage 2 proof is only available through the bound migration ceremony.
            return LoginResult.InvalidCredentials();
        }

        return _directoryIntegrationOptions.Enabled
            ? await AuthenticateStage1ProviderUserAsync(
                login,
                password,
                existingAliasUser,
                existingAliasBinding,
                cancellationToken)
            : await AuthenticateContractUserAsync(login, password, null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(bool Succeeded, string? Error)> CanLinkExternalLoginAsync(ApplicationUser user, string provider, CancellationToken cancellationToken = default)
    {
        if (_externalLoginOptions.MaxLoginsPerProvider <= 0)
        {
            return (true, null);
        }

        var existingLogins = await _userManager.GetLoginsAsync(user);
        var existingCount = existingLogins.Count(l => l.LoginProvider == provider);

        if (existingCount >= _externalLoginOptions.MaxLoginsPerProvider)
        {
             return (false, $"You have reached the maximum number of linked accounts ({_externalLoginOptions.MaxLoginsPerProvider}) for {provider}.");
        }

        return (true, null);
    }

    public async Task<LoginResult> ValidateExternalUserSignInAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (!user.IsActive || user.IsDeleted)
        {
            LogUserDeactivated(user.UserName);
            return LoginResult.UserInactive();
        }

        var personCheckResult = await ValidatePersonStatusAsync(user, cancellationToken);
        if (personCheckResult != null)
        {
            return personCheckResult;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            LogUserLockedOut(user.UserName);
            return LoginResult.LockedOut();
        }

        return LoginResult.Success(user);
    }

    private async Task<LoginResult> AuthenticateLocalUserAsync(ApplicationUser user, string password, CancellationToken cancellationToken)
    {
        // Check if user account is active
        if (!user.IsActive)
        {
            LogUserDeactivated(user.UserName);
            return LoginResult.UserInactive();
        }

        // Phase 18: Check Person status before authentication
        var personCheckResult = await ValidatePersonStatusAsync(user, cancellationToken);
        if (personCheckResult != null)
        {
            return personCheckResult;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            LogUserLockedOut(user.UserName);
            return LoginResult.LockedOut();
        }

        if (await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.ResetAccessFailedCountAsync(user);
            var currentPolicy = await _securityPolicyService.GetCurrentPolicyAsync();
            if (LocalPasswordSignInPolicy.RequiresChange(
                    user,
                    currentPolicy,
                    _timeProvider.GetUtcNow().UtcDateTime))
            {
                return LoginResult.PasswordChangeRequired(user);
            }

            LogUserAuthenticated(user.UserName);
            return LoginResult.Success(user);
        }

        // Password is incorrect, handle lockout logic
        LogInvalidPasswordAttempt(user.UserName);
        
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        if (policy.MaxFailedAccessAttempts > 0)
        {
            await _userManager.AccessFailedAsync(user);
            var accessFailedCount = await _userManager.GetAccessFailedCountAsync(user);

            if (accessFailedCount >= policy.MaxFailedAccessAttempts)
            {
                LogUserLockedOutCheck(user.UserName);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(policy.LockoutDurationMinutes));
                return LoginResult.LockedOut();
            }
        }
        
        return LoginResult.InvalidCredentials();
    }

    private async Task<LoginResult> AuthenticateContractUserAsync(
        string login, string password, ApplicationUser? existingUser, string? boundSubject,
        CancellationToken cancellationToken)
    {
        if (!_proofOptions.Enabled || _proofProvider is null ||
            string.IsNullOrWhiteSpace(_proofOptions.TrustedProviderNamespace)) return LoginResult.InvalidCredentials();
        if (existingUser is not null)
        {
            var eligibility = await ValidateExternalUserSignInAsync(existingUser, cancellationToken);
            if (!eligibility.IsSuccess) return eligibility;
        }
        ProofResult proof;
        try { proof = await _proofProvider.ProveAsync(new ProofRequest { AccountName = login }, password, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return LoginResult.InvalidCredentials(); }
        if (!proof.TryValidate(out _) || proof.Outcome != ProofOutcome.Authenticated ||
            proof.RequiredActions.Count != 0 || proof.ProviderNamespace != _proofOptions.TrustedProviderNamespace ||
            (boundSubject is not null && proof.StableSubject != boundSubject)) return LoginResult.InvalidCredentials();

        var linked = await _userManager.FindByLoginAsync(proof.ProviderNamespace!, proof.StableSubject!);
        if (linked is not null)
        {
            var links = await _userManager.GetLoginsAsync(linked);
            if (!links.Any(l => l.LoginProvider == proof.ProviderNamespace && l.ProviderKey == proof.StableSubject) ||
                _credentialMigrationStateStore is null ||
                await _credentialMigrationStateStore.FindAsync(linked.Id, cancellationToken) is not null)
                return LoginResult.InvalidCredentials();
            var eligibility = await ValidateExternalUserSignInAsync(linked, cancellationToken);
            if (!eligibility.IsSuccess) return eligibility;
        }

        // Mailbox ownership is separate from display-profile assurance. Never auto-link by this email.
        var profile = proof.Profile;
        var provisioned = await _jitProvisioningService.ProvisionExternalUserAsync(new ExternalAuthResult
        {
            Provider = proof.ProviderNamespace!, ProviderKey = proof.StableSubject!,
            DisplayName = profile?.DisplayName, FirstName = profile?.GivenName, LastName = profile?.Surname,
            Department = profile?.Department, JobTitle = profile?.Title, EmployeeId = profile?.EmployeeId
        }, cancellationToken);
        if (existingUser is not null && provisioned.Id != existingUser.Id) return LoginResult.InvalidCredentials();
        var result = await ValidateExternalUserSignInAsync(provisioned, cancellationToken);
        if (!result.IsSuccess) return result;
        await RefreshProviderProfileAsync(provisioned, proof.ProviderNamespace!, proof.StableSubject!, cancellationToken);
        return LoginResult.LegacySuccess(provisioned);
    }

    private async Task RefreshProviderProfileAsync(ApplicationUser user, string providerNamespace,
        string stableSubject, CancellationToken cancellationToken)
    {
        if (_providerProfileService is null) return;
        await _providerProfileService.RefreshAfterLoginAsync(user, providerNamespace, stableSubject, cancellationToken);
    }

    private async Task<LoginResult> AuthenticateStage1ProviderUserAsync(
        string login,
        string password,
        ApplicationUser? existingAliasUser,
        ProviderSubjectDirectoryBinding? existingAliasBinding,
        CancellationToken cancellationToken)
    {
        if (_proofProvider is null || _directoryIdentityLookup is null || _stage1BindingRefreshService is null)
        {
            return LoginResult.InvalidCredentials();
        }

        ProofResult proof;
        var hasDurableBinding = existingAliasBinding is not null;
        try
        {
            proof = await _proofProvider.ProveAsync(
                new ProofRequest { AccountName = login },
                password,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LoginResult.InvalidCredentials();
        }

        if (!proof.TryValidate(out _) || proof.Outcome != ProofOutcome.Authenticated || proof.RequiredActions.Count != 0)
        {
            return LoginResult.InvalidCredentials();
        }

        if (existingAliasBinding is not null &&
            (existingAliasUser is null ||
             existingAliasUser.Id != existingAliasBinding.LocalAccountId ||
             !string.Equals(existingAliasBinding.ProviderNamespace, proof.ProviderNamespace, StringComparison.Ordinal) ||
             !string.Equals(existingAliasBinding.StableSubject, proof.StableSubject, StringComparison.Ordinal)))
        {
            return LoginResult.InvalidCredentials();
        }

        var linkedUser = await _userManager.FindByLoginAsync(proof.ProviderNamespace!, proof.StableSubject!);
        if (linkedUser is not null)
        {
            var exactLinks = await _userManager.GetLoginsAsync(linkedUser);
            if (!exactLinks.Any(l => l.LoginProvider == proof.ProviderNamespace && l.ProviderKey == proof.StableSubject) ||
                _credentialMigrationStateStore is null ||
                await _credentialMigrationStateStore.FindAsync(linkedUser.Id, cancellationToken) is not null)
                return LoginResult.InvalidCredentials();
        }

        var provisionedUser = existingAliasUser ?? await _jitProvisioningService.ProvisionExternalUserAsync(
            new ExternalAuthResult
            {
                Provider = proof.ProviderNamespace!,
                ProviderKey = proof.StableSubject!
            },
            cancellationToken);

        if (!provisionedUser.IsActive || provisionedUser.IsDeleted)
        {
            LogUserDeactivated(provisionedUser.UserName);
            return LoginResult.UserInactive();
        }

        var personCheckResult = await ValidatePersonStatusAsync(provisionedUser, cancellationToken);
        if (personCheckResult is not null)
        {
            return personCheckResult;
        }

        try
        {
            var lookup = await _directoryIdentityLookup.FindManagedIdentityAsync(
                proof.CanonicalAccount!,
                cancellationToken);
            if (lookup.Outcome == DirectoryLookupOutcome.Found && lookup.Identity is not null)
            {
                if (existingAliasBinding is not null &&
                    existingAliasBinding.DirectoryObjectId != lookup.Identity.ObjectId)
                {
                    return LoginResult.InvalidCredentials();
                }

                var refresh = await _stage1BindingRefreshService.BindAndRefreshAsync(
                    new Stage1BindingRefreshRequest(
                        provisionedUser.Id,
                        proof.ProviderNamespace!,
                        proof.StableSubject!,
                        lookup.Identity),
                    cancellationToken);
                if (existingAliasBinding is not null && refresh == Stage1BindingRefreshOutcome.Conflict)
                {
                    return LoginResult.InvalidCredentials();
                }

                hasDurableBinding = refresh is
                    Stage1BindingRefreshOutcome.BoundAndRefreshed or
                    Stage1BindingRefreshOutcome.ExistingBindingRefreshed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            LogStage1DirectoryRefreshFailed();
        }

        if (hasDurableBinding)
        {
            await TryRefreshProviderMetadataAsync(
                provisionedUser,
                proof.ProviderNamespace!,
                proof.StableSubject!,
                cancellationToken);
        }

        await RefreshProviderProfileAsync(provisionedUser, proof.ProviderNamespace!, proof.StableSubject!, cancellationToken);
        return LoginResult.LegacySuccess(provisionedUser);
    }

    private async Task<LoginResult> AuthenticateCompletedDirectoryUserAsync(
        ApplicationUser user,
        string password,
        DirectoryObjectBinding binding,
        CancellationToken cancellationToken)
    {
        if (_stage2CredentialMigrationService is null)
        {
            return LoginResult.InvalidCredentials();
        }

        var directory = await _stage2CredentialMigrationService.AuthenticateCompletedAsync(
            user.Id,
            password,
            cancellationToken);
        if (directory.Outcome is DirectoryCredentialOutcome.Authenticated or
                DirectoryCredentialOutcome.PasswordChangeRequired &&
            user.RequiresPasswordChange &&
            await ValidateExternalUserSignInAsync(user, cancellationToken) is { IsSuccess: true })
        {
            return LoginResult.DirectoryPasswordChangeRequired(user, binding.DirectoryObjectId);
        }

        if (directory.Outcome != DirectoryCredentialOutcome.Authenticated)
        {
            return LoginResult.InvalidCredentials();
        }

        // Keep the existing local lifecycle and local lockout rules after directory credential proof.
        var result = await ValidateExternalUserSignInAsync(user, cancellationToken);
        if (result.IsSuccess)
        {
            await TryRefreshProviderMetadataAsync(
                user,
                binding.ProviderNamespace,
                binding.StableSubject,
                cancellationToken);
        }

        return result;
    }

    private async Task TryRefreshProviderMetadataAsync(
        ApplicationUser user,
        string providerNamespace,
        string stableSubject,
        CancellationToken cancellationToken)
    {
        if (_providerMetadataRefreshService is null || !user.IsActive || user.IsDeleted)
        {
            return;
        }

        try
        {
            if (await _userManager.IsLockedOutAsync(user))
            {
                return;
            }

            await _providerMetadataRefreshService.RefreshAsync(
                providerNamespace,
                stableSubject,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            LogProviderMetadataRefreshFailed();
        }
    }

    private bool IsDirectoryAuthenticationEnabled() =>
        _directoryIntegrationOptions.Enabled &&
        _directoryIntegrationOptions.AuthenticationEnabled;

    private bool IsStage2Enabled() =>
        IsDirectoryAuthenticationEnabled() &&
        _credentialMigrationOptions.Enabled;

    private async Task<CanonicalAliasResolution> ResolveCanonicalAliasAsync(
        string accountName,
        CancellationToken cancellationToken)
    {
        var normalizedAlias = ProviderSubjectDirectoryBinding.NormalizeCanonicalAccountAlias(accountName);
        if (normalizedAlias is null)
        {
            return CanonicalAliasResolution.NotFound;
        }

        var bindings = await _dbContext.ProviderSubjectDirectoryBindings
            .AsNoTracking()
            .Where(binding => binding.NormalizedCanonicalAccountAlias == normalizedAlias)
            .ToListAsync(cancellationToken);
        if (bindings.Count == 0)
        {
            return CanonicalAliasResolution.NotFound;
        }

        if (bindings.Count != 1)
        {
            return CanonicalAliasResolution.Conflicting;
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == bindings[0].LocalAccountId, cancellationToken);
        return user is null
            ? CanonicalAliasResolution.Conflicting
            : new CanonicalAliasResolution(user, bindings[0], false);
    }

    private sealed record CanonicalAliasResolution(
        ApplicationUser? User,
        ProviderSubjectDirectoryBinding? Binding,
        bool Conflict)
    {
        public bool IsResolved => User is not null && Binding is not null;
        public static CanonicalAliasResolution NotFound { get; } = new(null, null, false);
        public static CanonicalAliasResolution Conflicting { get; } = new(null, null, true);
    }

    /// <summary>
    /// Validates if the user's linked Person is allowed to authenticate.
    /// Phase 18: Personnel Lifecycle Management
    /// </summary>
    private async Task<LoginResult?> ValidatePersonStatusAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // If user is not linked to a Person, allow login (no Person-level restrictions)
        if (user.PersonId == null)
        {
            return null;
        }

        // Load the Person from the database
        var person = await _dbContext.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == user.PersonId, cancellationToken);

        if (person == null)
        {
            // Person was deleted - block login
            LogPersonNotFound(user.UserName, user.PersonId.Value);
            return LoginResult.PersonInactive("Associated person record not found");
        }

        // Use the Person.CanAuthenticate() helper method
        if (!person.CanAuthenticate())
        {
            var reason = GetPersonInactiveReason(person);
            LogPersonInactive(user.UserName, person.Id, reason);
            return LoginResult.PersonInactive(reason);
        }

        // Copy Person's Locale to User if User doesn't have one
        // (Don't attach Person to User navigation property as it causes tracking conflicts)
        if (string.IsNullOrEmpty(user.Locale) && !string.IsNullOrEmpty(person.Locale))
        {
            user.Locale = person.Locale;
        }

        return null; // Person is valid, continue with login
    }

    /// <summary>
    /// Gets a user-friendly reason why the Person cannot authenticate.
    /// </summary>
    private string GetPersonInactiveReason(Core.Domain.Entities.Person person)
    {
        if (person.IsDeleted)
            return "Person record has been deleted";
        
        if (person.Status != PersonStatus.Active)
            return $"Person status is {person.Status}";
        
        var now = _timeProvider.GetUtcNow().Date;
        if (person.StartDate.HasValue && person.StartDate.Value.Date > now)
            return "Person employment has not started yet";
        
        if (person.EndDate.HasValue && person.EndDate.Value.Date < now)
            return "Person employment has ended";
        
        return "Person is not active";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account '{UserName}' is locked out.")]
    partial void LogUserLockedOut(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' authenticated successfully.")]
    partial void LogUserAuthenticated(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid password attempt for user '{UserName}'.")]
    partial void LogInvalidPasswordAttempt(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account '{UserName}' locked out due to too many failed login attempts.")]
    partial void LogUserLockedOutCheck(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{Login}' authenticated via legacy auth and JIT provisioned.")]
    partial void LogLegacyUserAuthenticated(string login);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for user '{UserName}': Person {PersonId} not found.")]
    partial void LogPersonNotFound(string? userName, Guid personId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for user '{UserName}': Person {PersonId} is inactive - {Reason}.")]
    partial void LogPersonInactive(string? userName, Guid personId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for user '{UserName}': User account is deactivated.")]
    partial void LogUserDeactivated(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stage 1 directory binding or profile refresh did not complete.")]
    partial void LogStage1DirectoryRefreshFailed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider metadata refresh did not complete after successful authentication.")]
    partial void LogProviderMetadataRefreshFailed();
}

