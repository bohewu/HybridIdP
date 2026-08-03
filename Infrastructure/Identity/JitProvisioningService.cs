using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
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

    public JitProvisioningService(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IOptions<ExternalLoginOptions> externalLoginOptions)
    {
        _userManager = userManager;
        _context = context;
        _externalLoginOptions = externalLoginOptions.Value;
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
            if (hasVerifiedEmail)
            {
                existingUser.Email = externalAuth.Email;
                existingUser.EmailConfirmed = true;
            }
            existingUser.FirstName = externalAuth.FirstName ?? existingUser.FirstName;
            existingUser.LastName = externalAuth.LastName ?? existingUser.LastName;
            existingUser.MiddleName = externalAuth.MiddleName ?? existingUser.MiddleName;
            existingUser.PhoneNumber = externalAuth.PhoneNumber ?? existingUser.PhoneNumber;
            existingUser.Department = externalAuth.Department ?? existingUser.Department;
            existingUser.JobTitle = externalAuth.JobTitle ?? existingUser.JobTitle;
            existingUser.EmployeeId = externalAuth.EmployeeId ?? existingUser.EmployeeId;
            existingUser.ModifiedAt = DateTime.UtcNow;
            
            await _userManager.UpdateAsync(existingUser);
            
            // Also update linked Person if exists
            if (existingUser.PersonId.HasValue)
            {
                var existingPerson = await _context.Persons.FindAsync([existingUser.PersonId.Value], cancellationToken);
                if (existingPerson != null)
                {
                    // Update Person fields
                    if (hasVerifiedEmail)
                    {
                        existingPerson.Email = externalAuth.Email;
                    }
                    existingPerson.PhoneNumber = externalAuth.PhoneNumber ?? existingPerson.PhoneNumber;
                    existingPerson.FirstName = externalAuth.FirstName ?? existingPerson.FirstName;
                    existingPerson.LastName = externalAuth.LastName ?? existingPerson.LastName;
                    existingPerson.MiddleName = externalAuth.MiddleName ?? existingPerson.MiddleName;
                    existingPerson.EmployeeId = externalAuth.EmployeeId ?? existingPerson.EmployeeId;
                    existingPerson.Department = externalAuth.Department ?? existingPerson.Department;
                    existingPerson.JobTitle = externalAuth.JobTitle ?? existingPerson.JobTitle;
                    
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
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            
            return existingUser;
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
            person.FirstName = externalAuth.FirstName ?? person.FirstName;
            person.LastName = externalAuth.LastName ?? person.LastName;
            person.MiddleName = externalAuth.MiddleName ?? person.MiddleName;
            person.EmployeeId = externalAuth.EmployeeId ?? person.EmployeeId;
            person.Department = externalAuth.Department ?? person.Department;
            person.JobTitle = externalAuth.JobTitle ?? person.JobTitle;
            
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
        username ??= $"{externalAuth.Provider}_{externalAuth.ProviderKey}";
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
                FirstName = externalAuth.FirstName,
                LastName = externalAuth.LastName,
                MiddleName = externalAuth.MiddleName,
                Department = externalAuth.Department,
                JobTitle = externalAuth.JobTitle,
                EmployeeId = externalAuth.EmployeeId,
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
                await _userManager.UpdateAsync(newUser);
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

        // Step 6: Save Person changes
        await _context.SaveChangesAsync(cancellationToken);

        return newUser;
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
        if (externalAuth.EmailVerified && !string.IsNullOrWhiteSpace(externalAuth.Email))
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
