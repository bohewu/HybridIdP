using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Identity;

public class JitProvisioningService : IJitProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly ExternalLoginOptions _externalLoginOptions;
    private readonly INativeFieldPolicyService _nativeFields;

    public JitProvisioningService(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IOptions<ExternalLoginOptions> externalLoginOptions,
        INativeFieldPolicyService nativeFields)
    {
        _userManager = userManager;
        _context = context;
        _externalLoginOptions = externalLoginOptions.Value;
        _nativeFields = nativeFields;
    }

    public async Task<ApplicationUser> ProvisionExternalUserAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken = default)
    {
        // Validation
        ArgumentNullException.ThrowIfNull(externalAuth);
        if (string.IsNullOrWhiteSpace(externalAuth.Provider))
            throw new ArgumentException("Provider is required", nameof(externalAuth));
        if (string.IsNullOrWhiteSpace(externalAuth.ProviderKey))
            throw new ArgumentException("ProviderKey is required", nameof(externalAuth));

        var hasVerifiedEmail =
            externalAuth.EmailVerified &&
            !string.IsNullOrWhiteSpace(externalAuth.Email);
        var hasAssuredContactEmail = externalAuth.AssuredCanonicalAccount is not null &&
            !string.IsNullOrWhiteSpace(externalAuth.Email);

        // Step 1: Check if this external login already exists
        var existingUser = await _userManager.FindByLoginAsync(
            externalAuth.Provider,
            externalAuth.ProviderKey
        );

        if (existingUser != null)
        {
            if (!existingUser.IsActive || existingUser.IsDeleted)
            {
                throw new InvalidOperationException("User account is unavailable.");
            }

            // Already exists, update ApplicationUser information
            // A verified provider address retains its assurance. A display-only
            // contact may only fill a missing address; neither retargets Email MFA.
            if ((hasVerifiedEmail || hasAssuredContactEmail && string.IsNullOrWhiteSpace(existingUser.Email)) &&
                !existingUser.EmailMfaEnabled)
            {
                if (!string.Equals(existingUser.Email, externalAuth.Email, StringComparison.Ordinal))
                {
                    existingUser.EmailMfaCode = null;
                    existingUser.EmailMfaCodeExpiry = null;
                    existingUser.EmailMfaVerificationAttempts = 0;
                }
                existingUser.Email = externalAuth.Email;
                existingUser.EmailConfirmed = hasVerifiedEmail;
            }
            existingUser.MiddleName = externalAuth.MiddleName ?? existingUser.MiddleName;
            existingUser.PhoneNumber = externalAuth.PhoneNumber ?? existingUser.PhoneNumber;
            existingUser.ModifiedAt = DateTime.UtcNow;
            Person? existingPerson = null;
            try
            {
                // Also update linked Person if exists
                if (existingUser.PersonId.HasValue)
                {
                    existingPerson = await _context.Persons.FindAsync([existingUser.PersonId.Value], cancellationToken);
                    if (existingPerson != null)
                    {
                        // Update Person fields
                        if (hasVerifiedEmail || hasAssuredContactEmail && string.IsNullOrWhiteSpace(existingPerson.Email))
                        {
                            existingPerson.Email = externalAuth.Email;
                        }
                        existingPerson.PhoneNumber = externalAuth.PhoneNumber ?? existingPerson.PhoneNumber;
                        existingPerson.MiddleName = externalAuth.MiddleName ?? existingPerson.MiddleName;
                    
                        // Update PID fields only if newly provided and currently empty
                        if (!string.IsNullOrWhiteSpace(externalAuth.NationalId) && string.IsNullOrWhiteSpace(existingPerson.NationalId))
                        {
                            existingPerson.NationalId = PidHasher.Hash(externalAuth.NationalId);
                        }
                    
                        // Auto-verify Legacy users if not already verified
                        if (externalAuth.Provider == Core.Domain.Constants.AuthConstants.Providers.Legacy && existingPerson.IdentityVerifiedAt == null)
                        {
                            existingPerson.IdentityVerifiedAt = DateTime.UtcNow;
                            if (string.IsNullOrWhiteSpace(existingPerson.IdentityDocumentType) && !string.IsNullOrWhiteSpace(existingPerson.NationalId))
                            {
                                existingPerson.IdentityDocumentType = "NationalId";
                            }
                        }
                    
                        existingPerson.ModifiedAt = DateTime.UtcNow;
                    }
                }
                await ApplyNativeFieldsAsync(existingUser, existingPerson, externalAuth, cancellationToken);
                // Identity saves both tracked targets and their policy state atomically.
                var updateResult = await _userManager.UpdateAsync(existingUser);
                if (!updateResult.Succeeded)
                    throw new InvalidOperationException("Failed to update external user profile.");
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                _context.Detach(existingUser);
                if (existingPerson is not null) _context.Detach(existingPerson);
                DetachNativePolicyGuards();
                throw;
            }
            
            return existingUser;
        }

        if (externalAuth.AssuredCanonicalAccount is not null)
        {
            return await ProvisionCanonicalAccountAsync(externalAuth, cancellationToken);
        }

        var username = hasVerifiedEmail
            ? externalAuth.Email!
            : null;
        var usernameUser = hasVerifiedEmail
            ? await _userManager.FindByNameAsync(username!)
            : null;

        if (usernameUser != null && (!usernameUser.IsActive || usernameUser.IsDeleted))
        {
            throw new InvalidOperationException("User account is unavailable.");
        }

        if (usernameUser != null && (externalAuth.RequireNewAccount || !_externalLoginOptions.AutoLinkMatchingEmail ||
            !string.Equals(usernameUser.Email, externalAuth.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("An existing account requires explicit linking.");
        }

        // Step 2: Try to find existing Person by identity documents first, then by Email
        Person? person = await FindExistingPersonAsync(externalAuth, cancellationToken);

         // Step 3: If no Person found, create new one; otherwise update existing
        if (person == null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                Email = hasVerifiedEmail ? externalAuth.Email : null,
                PhoneNumber = externalAuth.PhoneNumber,
                FirstName = externalAuth.FirstName,
                LastName = externalAuth.LastName,
                MiddleName = externalAuth.MiddleName,
                EmployeeId = externalAuth.EmployeeId,
                Department = externalAuth.Department,
                JobTitle = externalAuth.JobTitle,
                // Hash PID values before storing
                NationalId = PidHasher.Hash(externalAuth.NationalId),
                PassportNumber = PidHasher.Hash(externalAuth.PassportNumber),
                ResidentCertificateNumber = PidHasher.Hash(externalAuth.ResidentCertificateNumber),
                // Auto-verify Legacy users since they're trusted from the old system
                IdentityVerifiedAt = externalAuth.Provider == Core.Domain.Constants.AuthConstants.Providers.Legacy ? DateTime.UtcNow : null,
                IdentityDocumentType = !string.IsNullOrWhiteSpace(externalAuth.NationalId) ? "NationalId" : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = null // System provisioned
            };
            await _context.Persons.AddAsync(person, cancellationToken);
        }
        else
        {
            // Update existing Person with new data from auth provider (if provided)
            if (hasVerifiedEmail)
            {
                person.Email = externalAuth.Email;
            }
            person.PhoneNumber = externalAuth.PhoneNumber ?? person.PhoneNumber;
            person.MiddleName = externalAuth.MiddleName ?? person.MiddleName;
            
            // Only update PID fields if newly provided and currently empty
            if (!string.IsNullOrWhiteSpace(externalAuth.NationalId) && string.IsNullOrWhiteSpace(person.NationalId))
            {
                person.NationalId = PidHasher.Hash(externalAuth.NationalId);
            }
            if (!string.IsNullOrWhiteSpace(externalAuth.PassportNumber) && string.IsNullOrWhiteSpace(person.PassportNumber))
            {
                person.PassportNumber = PidHasher.Hash(externalAuth.PassportNumber);
            }
            if (!string.IsNullOrWhiteSpace(externalAuth.ResidentCertificateNumber) && string.IsNullOrWhiteSpace(person.ResidentCertificateNumber))
            {
                person.ResidentCertificateNumber = PidHasher.Hash(externalAuth.ResidentCertificateNumber);
            }
            
            // Auto-verify Legacy users if not already verified
            if (externalAuth.Provider == Core.Domain.Constants.AuthConstants.Providers.Legacy && person.IdentityVerifiedAt == null)
            {
                person.IdentityVerifiedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(person.IdentityDocumentType) && !string.IsNullOrWhiteSpace(person.NationalId))
                {
                    person.IdentityDocumentType = "NationalId";
                }
            }
            
            person.ModifiedAt = DateTime.UtcNow;
        }

        // Step 4: Create new ApplicationUser (linked to Person)
        var providerUserName = $"{externalAuth.Provider}_{externalAuth.ProviderKey}";
        var allowedCharacters = _userManager.Options.User.AllowedUserNameCharacters;
        if (providerUserName.Length > 256 ||
            (!string.IsNullOrEmpty(allowedCharacters) && providerUserName.Any(c => !allowedCharacters.Contains(c))))
        {
            // Local usernames are constrained presentation identifiers, not provider identity keys.
            // Keep the opaque tuple unchanged in the external-login link, including repeat login.
            providerUserName = "provider_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] { externalAuth.Provider, externalAuth.ProviderKey })));
        }
        username ??= providerUserName;
        var newUser = usernameUser;
        if (newUser == null)
        {
            newUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = username,
                Email = externalAuth.Email,
                EmailConfirmed = hasVerifiedEmail,
                PersonId = person.Id,
                MiddleName = externalAuth.MiddleName,
                PhoneNumber = externalAuth.PhoneNumber,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(newUser);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}"
                );
            }

            await AssignDefaultRoleIfConfiguredAsync(newUser);
        }
        else
        {
            // User exists but wasn't found by FindByLogin. Ensure Person is linked.
            if (newUser.PersonId == null)
            {
                newUser.PersonId = person.Id;
                var linkPersonResult = await _userManager.UpdateAsync(newUser);
                if (!linkPersonResult.Succeeded)
                {
                    _context.Detach(newUser);
                    _context.Detach(person);
                    throw new InvalidOperationException("Failed to link external user profile.");
                }
            }
        }

        // Step 5: Create external login link
        var displayName = externalAuth.DisplayName ?? 
                         externalAuth.Email ?? 
                         externalAuth.ProviderKey;
        
        var addLoginResult = await _userManager.AddLoginAsync(
            newUser,
            new UserLoginInfo(
                externalAuth.Provider,
                externalAuth.ProviderKey,
                displayName
            )
        );

        if (!addLoginResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add external login: {string.Join(", ", addLoginResult.Errors.Select(e => e.Description))}"
            );
        }

        // Persist the exact login before evaluating native source eligibility.
        await _context.SaveChangesAsync(cancellationToken);
        try
        {
            await ApplyNativeFieldsAsync(newUser, person, externalAuth, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _context.Detach(newUser);
            _context.Detach(person);
            DetachNativePolicyGuards();
            throw;
        }

        return newUser;
    }

    private async Task<ApplicationUser> ProvisionCanonicalAccountAsync(
        ExternalAuthResult externalAuth, CancellationToken cancellationToken)
    {
        ProofUserNameStrategy strategy;
        try { strategy = await _nativeFields.GetProofUserNameStrategyAsync(externalAuth.Provider, cancellationToken); }
        catch (NativeFieldPolicyException) { throw new InvalidOperationException("Provider naming policy is unavailable."); }
        var username = strategy == ProofUserNameStrategy.Email ? externalAuth.Email : externalAuth.AssuredCanonicalAccount;
        if (strategy == ProofUserNameStrategy.Email && (!externalAuth.ProofEmailAssured ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(username)))
            throw new InvalidOperationException("Provider email name is unavailable.");
        var contactEmail = string.IsNullOrWhiteSpace(externalAuth.Email) ? null : externalAuth.Email;
        var allowedCharacters = _userManager.Options.User.AllowedUserNameCharacters;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 256 || username != username.Trim() || username.Any(char.IsControl) ||
            !ProofLoginHint.ValidAccount(externalAuth.AssuredCanonicalAccount) ||
            (!string.IsNullOrEmpty(allowedCharacters) && username.Any(c => !allowedCharacters.Contains(c))) ||
            await _userManager.FindByNameAsync(username) is not null)
        {
            throw new InvalidOperationException("Provider account name is unavailable.");
        }

        // Proof naming never matches a Person or account by email/name. Persist the
        // new Person, account, default role and exact login together on relational stores.
        var relationalDb = _context is DbContext db && db.Database.IsRelational() ? db : null;
        var emailNaming = strategy == ProofUserNameStrategy.Email;
        await using var transaction = relationalDb is null ? null : emailNaming
            ? await relationalDb.Database.BeginTransactionAsync(relationalDb.Database.IsNpgsql()
                ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.Serializable, cancellationToken)
            : await relationalDb.Database.BeginTransactionAsync(cancellationToken);
        if (emailNaming)
        {
            // SQL Server serializable reads protect the email range through commit.
            // PostgreSQL SSI does not block ordinary existing-account writers: lock
            // their table first, then read a fresh Read Committed snapshot. The lock
            // also serializes competing creators without a shared-lock upgrade.
            if (relationalDb is not null && relationalDb.Database.IsNpgsql())
                await relationalDb.Database.ExecuteSqlRawAsync(
                    "LOCK TABLE \"AspNetUsers\" IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
            var normalizedEmail = _userManager.NormalizeEmail(username);
            if (await _context.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
                throw new InvalidOperationException("Provider email name is unavailable.");
        }

        var person = new Person
        {
            Id = Guid.NewGuid(), Email = contactEmail, CreatedAt = DateTime.UtcNow
        };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = username, Email = contactEmail,
            EmailConfirmed = externalAuth.EmailVerified && contactEmail is not null, PersonId = person.Id,
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
        try
        {
            await _context.Persons.AddAsync(person, cancellationToken);
            var created = await _userManager.CreateAsync(user);
            if (!created.Succeeded) throw new InvalidOperationException("Provider account creation failed.");
            await AssignDefaultRoleIfConfiguredAsync(user);
            var linked = await _userManager.AddLoginAsync(user, new UserLoginInfo(
                externalAuth.Provider, externalAuth.ProviderKey, externalAuth.DisplayName ?? username));
            if (!linked.Succeeded) throw new InvalidOperationException("Provider account binding failed.");
            await _context.SaveChangesAsync(cancellationToken);
            await ApplyNativeFieldsAsync(user, person, externalAuth, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await ProofLoginHint.SaveAsync(_userManager, user, externalAuth.Provider, externalAuth.ProviderKey,
                externalAuth.AssuredCanonicalAccount!);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return user;
        }
        catch
        {
            DetachNativePolicyGuards();
            // A failed request must not flush its pending account/link on a later save.
            if (_context is DbContext failedDb)
            {
                foreach (var entry in failedDb.ChangeTracker.Entries().Where(entry =>
                    ReferenceEquals(entry.Entity, user) || ReferenceEquals(entry.Entity, person) ||
                    entry.Entity is IdentityUserLogin<Guid> login && login.UserId == user.Id ||
                    entry.Entity is IdentityUserRole<Guid> role && role.UserId == user.Id ||
                    entry.Entity is IdentityUserToken<Guid> token && token.UserId == user.Id).ToList())
                    entry.State = EntityState.Detached;
            }
            throw;
        }
    }

    private async Task ApplyNativeFieldsAsync(ApplicationUser user, Person? person,
        ExternalAuthResult auth, CancellationToken cancellationToken)
    {
        var values = new Dictionary<NativeField, string?>
        {
            [NativeField.FirstName] = auth.FirstName, [NativeField.LastName] = auth.LastName,
            [NativeField.Department] = auth.Department, [NativeField.JobTitle] = auth.JobTitle,
            [NativeField.EmployeeId] = auth.EmployeeId
        };
        if (auth.AssuredCanonicalAccount is not null)
        {
            values[NativeField.Nickname] = auth.DisplayName;
            await _nativeFields.ApplySourceAsync(user, person,
                new(NativeFieldSource.Proof, user.Id, auth.Provider, auth.ProviderKey), values, cancellationToken);
        }
        else
        {
            _nativeFields.ApplyUnattributed(user, person, values);
        }
    }

    private void DetachNativePolicyGuards()
    {
        if (_context is DbContext db)
            foreach (var entry in db.ChangeTracker.Entries<NativeFieldSourcePolicy>().ToArray())
                entry.State = EntityState.Detached;
    }

    private async Task AssignDefaultRoleIfConfiguredAsync(ApplicationUser user)
    {
        var defaultRole = _externalLoginOptions.AutoProvisionDefaultRole?.Trim();
        if (string.IsNullOrWhiteSpace(defaultRole))
        {
            return;
        }

        if (await _userManager.IsInRoleAsync(user, defaultRole))
        {
            return;
        }

        var roleResult = await _userManager.AddToRoleAsync(user, defaultRole);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign default role '{defaultRole}' to auto-provisioned user: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}"
            );
        }
    }

    /// <summary>
    /// Find existing Person by identity documents (priority) or Email (fallback)
    /// </summary>
    private async Task<Person?> FindExistingPersonAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken)
    {
        Guid? personId = null;
        
        // Priority 1: Match by identity documents (ANY match) - using hashed values
        if (!string.IsNullOrWhiteSpace(externalAuth.NationalId))
        {
            var hashedNationalId = PidHasher.Hash(externalAuth.NationalId);
            personId = await _context.Persons
                .AsNoTracking()
                .Where(p => p.NationalId == hashedNationalId)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (personId.HasValue)
                return await _context.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(externalAuth.PassportNumber))
        {
            var hashedPassport = PidHasher.Hash(externalAuth.PassportNumber);
            personId = await _context.Persons
                .AsNoTracking()
                .Where(p => p.PassportNumber == hashedPassport)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (personId.HasValue)
                return await _context.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(externalAuth.ResidentCertificateNumber))
        {
            var hashedResident = PidHasher.Hash(externalAuth.ResidentCertificateNumber);
            personId = await _context.Persons
                .AsNoTracking()
                .Where(p => p.ResidentCertificateNumber == hashedResident)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (personId.HasValue)
                return await _context.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, cancellationToken);
        }

        // Priority 2: Fallback to Email matching (if no identity documents or no match)
        if (_externalLoginOptions.AutoLinkMatchingEmail && externalAuth.EmailVerified &&
            !string.IsNullOrWhiteSpace(externalAuth.Email))
        {
            // Check Person.Email first
            personId = await _context.Persons
                .AsNoTracking()
                .Where(p => p.Email == externalAuth.Email)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (personId.HasValue)
                return await _context.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, cancellationToken);

            // Also check if any ApplicationUser with same PersonId has this email
            personId = await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == externalAuth.Email && u.PersonId != null)
                .Select(u => u.PersonId)
                .FirstOrDefaultAsync(cancellationToken);
            if (personId.HasValue)
                return await _context.Persons.FirstOrDefaultAsync(p => p.Id == personId.Value, cancellationToken);
        }

        return null;
    }

    [Obsolete("Use ProvisionExternalUserAsync instead")]
    public async Task<ApplicationUser> ProvisionUserAsync(
        LegacyUserDto dto,
        CancellationToken cancellationToken = default)
    {
        // Convert to ExternalAuthResult format
        var externalAuth = new ExternalAuthResult
        {
            Provider = Core.Domain.Constants.AuthConstants.Providers.Legacy,
            ProviderKey = dto.ExternalId ?? dto.Email ?? dto.NationalId ?? Guid.NewGuid().ToString(),
            Email = dto.Email,
            EmailVerified = true,
            FirstName = dto.FullName, // Legacy only has FullName
            PhoneNumber = dto.Phone,
            Department = dto.Department,
            JobTitle = dto.JobTitle,
            NationalId = dto.NationalId,
            PassportNumber = dto.PassportNumber,
            ResidentCertificateNumber = dto.ResidentCertificateNumber
        };

        return await ProvisionExternalUserAsync(externalAuth, cancellationToken);
    }
}
