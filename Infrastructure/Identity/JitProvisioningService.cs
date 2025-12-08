using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Identity;

public class JitProvisioningService : IJitProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;

    public JitProvisioningService(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
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

        // Step 1: Check if this external login already exists
        var existingUser = await _userManager.FindByLoginAsync(
            externalAuth.Provider,
            externalAuth.ProviderKey
        );

        if (existingUser != null)
        {
            // Already exists, update information
            existingUser.Email = externalAuth.Email ?? existingUser.Email;
            existingUser.FirstName = externalAuth.FirstName ?? existingUser.FirstName;
            existingUser.LastName = externalAuth.LastName ?? existingUser.LastName;
            existingUser.MiddleName = externalAuth.MiddleName ?? existingUser.MiddleName;
            existingUser.PhoneNumber = externalAuth.PhoneNumber ?? existingUser.PhoneNumber;
            existingUser.Department = externalAuth.Department ?? existingUser.Department;
            existingUser.JobTitle = externalAuth.JobTitle ?? existingUser.JobTitle;
            existingUser.EmployeeId = externalAuth.EmployeeId ?? existingUser.EmployeeId;
            existingUser.ModifiedAt = DateTime.UtcNow;
            
            await _userManager.UpdateAsync(existingUser);
            return existingUser;
        }

        // Step 2: Try to find existing Person by identity documents first, then by Email
        Person? person = await FindExistingPersonAsync(externalAuth, cancellationToken);

        // Step 3: If no Person found, create new one
        if (person == null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                Email = externalAuth.Email,
                PhoneNumber = externalAuth.PhoneNumber,
                FirstName = externalAuth.FirstName,
                LastName = externalAuth.LastName,
                MiddleName = externalAuth.MiddleName,
                EmployeeId = externalAuth.EmployeeId,
                Department = externalAuth.Department,
                JobTitle = externalAuth.JobTitle,
                NationalId = externalAuth.NationalId,
                PassportNumber = externalAuth.PassportNumber,
                ResidentCertificateNumber = externalAuth.ResidentCertificateNumber,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = null // System provisioned
            };
            await _context.Persons.AddAsync(person, cancellationToken);
        }

        // Step 4: Create new ApplicationUser (linked to Person)
        var username = externalAuth.Email ?? 
                      $"{externalAuth.Provider}_{externalAuth.ProviderKey}";
        
        var newUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            Email = externalAuth.Email,
            EmailConfirmed = !string.IsNullOrWhiteSpace(externalAuth.Email), // External auth is considered verified
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

    /// <summary>
    /// Find existing Person by identity documents (priority) or Email (fallback)
    /// </summary>
    private async Task<Person?> FindExistingPersonAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken)
    {
        // Priority 1: Match by identity documents (ANY match)
        if (!string.IsNullOrWhiteSpace(externalAuth.NationalId))
        {
            var personByNationalId = await _context.Persons
                .FirstOrDefaultAsync(p => p.NationalId == externalAuth.NationalId, cancellationToken);
            if (personByNationalId != null)
                return personByNationalId;
        }

        if (!string.IsNullOrWhiteSpace(externalAuth.PassportNumber))
        {
            var personByPassport = await _context.Persons
                .FirstOrDefaultAsync(p => p.PassportNumber == externalAuth.PassportNumber, cancellationToken);
            if (personByPassport != null)
                return personByPassport;
        }

        if (!string.IsNullOrWhiteSpace(externalAuth.ResidentCertificateNumber))
        {
            var personByResident = await _context.Persons
                .FirstOrDefaultAsync(p => p.ResidentCertificateNumber == externalAuth.ResidentCertificateNumber, cancellationToken);
            if (personByResident != null)
                return personByResident;
        }

        // Priority 2: Fallback to Email matching (if no identity documents or no match)
        if (!string.IsNullOrWhiteSpace(externalAuth.Email))
        {
            // Check Person.Email first
            var personByEmail = await _context.Persons
                .FirstOrDefaultAsync(p => p.Email == externalAuth.Email, cancellationToken);
            if (personByEmail != null)
                return personByEmail;

            // Also check if any ApplicationUser with same PersonId has this email
            var userWithEmail = await _context.Users
                .Include(u => u.Person)
                .FirstOrDefaultAsync(u => u.Email == externalAuth.Email && u.PersonId != null, cancellationToken);
            if (userWithEmail?.Person != null)
                return userWithEmail.Person;
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
            Provider = "Legacy",
            ProviderKey = dto.ExternalId ?? dto.Email ?? dto.NationalId ?? Guid.NewGuid().ToString(),
            Email = dto.Email,
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
