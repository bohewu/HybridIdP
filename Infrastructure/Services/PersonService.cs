using System.Text.Json;
using Core.Application;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Validators;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service implementation for managing Person entities and their relationships with ApplicationUsers.
/// Phase 10.2: Person Service & API
/// Phase 10.5: Added audit trail for all CRUD operations
/// Phase 10.6: Added identity document validation and verification
/// Phase 13: Added role synchronization for multi-account scenarios
/// </summary>
public partial class PersonService : IPersonService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PersonService> _logger;
    private readonly IAuditService _auditService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PersonService(
        IApplicationDbContext context,
        ILogger<PersonService> logger,
        IAuditService auditService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _userManager = userManager;
    }

    public async Task<Person?> GetPersonByIdAsync(Guid personId)
    {
        return await _context.Persons
            .Include(p => p.Accounts)
            .FirstOrDefaultAsync(p => p.Id == personId);
    }

    public async Task<Person?> GetPersonByEmployeeIdAsync(string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        return await _context.Persons
            .Include(p => p.Accounts)
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId);
    }

    public async Task<List<Person>> GetAllPersonsAsync(int skip = 0, int take = 50)
    {
        return await _context.Persons
            .Include(p => p.Accounts)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Person> CreatePersonAsync(Person person, Guid? createdBy = null)
    {
        // Set audit fields
        person.Id = Guid.NewGuid();
        person.CreatedAt = DateTime.UtcNow;
        person.CreatedBy = createdBy;
        person.ModifiedAt = null;
        person.ModifiedBy = null;

        // Normalize empty strings to null (prevents unique index violations and improves data consistency)
        person.Email = string.IsNullOrWhiteSpace(person.Email) ? null : person.Email;
        person.PhoneNumber = string.IsNullOrWhiteSpace(person.PhoneNumber) ? null : person.PhoneNumber;
        person.EmployeeId = string.IsNullOrWhiteSpace(person.EmployeeId) ? null : person.EmployeeId;
        person.NationalId = string.IsNullOrWhiteSpace(person.NationalId) ? null : person.NationalId;
        person.PassportNumber = string.IsNullOrWhiteSpace(person.PassportNumber) ? null : person.PassportNumber;
        person.ResidentCertificateNumber = string.IsNullOrWhiteSpace(person.ResidentCertificateNumber) ? null : person.ResidentCertificateNumber;

        // Validate EmployeeId uniqueness if provided
        if (!string.IsNullOrWhiteSpace(person.EmployeeId))
        {
            var existingPerson = await GetPersonByEmployeeIdAsync(person.EmployeeId);
            if (existingPerson != null)
            {
                throw new InvalidOperationException($"A person with EmployeeId '{person.EmployeeId}' already exists.");
            }
        }

        // Phase 10.6: Validate identity documents BEFORE hashing
        // We can only validate the format when we have the plaintext value
        if (!string.IsNullOrWhiteSpace(person.NationalId) && !PidHasher.IsHashed(person.NationalId))
        {
            if (!IdentityDocumentValidator.IsValidTaiwanNationalId(person.NationalId))
            {
                throw new InvalidOperationException($"Invalid Taiwan National ID format");
            }
        }

        if (!string.IsNullOrWhiteSpace(person.PassportNumber) && !PidHasher.IsHashed(person.PassportNumber))
        {
            if (!IdentityDocumentValidator.IsValidPassportNumber(person.PassportNumber))
            {
                throw new InvalidOperationException($"Invalid passport number format");
            }
        }

        if (!string.IsNullOrWhiteSpace(person.ResidentCertificateNumber) && !PidHasher.IsHashed(person.ResidentCertificateNumber))
        {
            if (!IdentityDocumentValidator.IsValidResidentCertificateNumber(person.ResidentCertificateNumber))
            {
                throw new InvalidOperationException($"Invalid resident certificate format");
            }
        }

        // Hash PID values for storage (only if not already hashed)
        var nationalIdHash = PidHasher.IsHashed(person.NationalId) ? person.NationalId : PidHasher.Hash(person.NationalId);
        var passportHash = PidHasher.IsHashed(person.PassportNumber) ? person.PassportNumber : PidHasher.Hash(person.PassportNumber);
        var residentCertHash = PidHasher.IsHashed(person.ResidentCertificateNumber) ? person.ResidentCertificateNumber : PidHasher.Hash(person.ResidentCertificateNumber);

        // Phase 10.6: Check for duplicate identity documents using hashed values
        var (isUnique, errorMessage) = await CheckPersonUniquenessAsync(
            nationalIdHash,
            passportHash,
            residentCertHash,
            null);

        if (!isUnique)
        {
            throw new InvalidOperationException(errorMessage);
        }

        // Store hashed values
        person.NationalId = nationalIdHash;
        person.PassportNumber = passportHash;
        person.ResidentCertificateNumber = residentCertHash;

        _context.Persons.Add(person);
        await _context.SaveChangesAsync(CancellationToken.None);

        LogPersonCreated(person.Id, person.EmployeeId ?? "N/A");

        // Phase 10.5: Audit the creation
        var auditDetails = JsonSerializer.Serialize(new
        {
            PersonId = person.Id,
            FirstName = person.FirstName,
            LastName = person.LastName,
            EmployeeId = person.EmployeeId,
            Department = person.Department
        });
        await _auditService.LogEventAsync(
            "PersonCreated",
            createdBy?.ToString(),
            auditDetails,
            null,
            null);

        return person;
    }

    public async Task<Person?> UpdatePersonAsync(Guid personId, Person person, Guid? modifiedBy = null)
    {
        var existingPerson = await GetPersonByIdAsync(personId);
        if (existingPerson == null)
            return null;

        // Normalize empty strings to null (prevents unique index violations and improves data consistency)
        person.Email = string.IsNullOrWhiteSpace(person.Email) ? null : person.Email;
        person.PhoneNumber = string.IsNullOrWhiteSpace(person.PhoneNumber) ? null : person.PhoneNumber;
        person.EmployeeId = string.IsNullOrWhiteSpace(person.EmployeeId) ? null : person.EmployeeId;
        person.NationalId = string.IsNullOrWhiteSpace(person.NationalId) ? null : person.NationalId;
        person.PassportNumber = string.IsNullOrWhiteSpace(person.PassportNumber) ? null : person.PassportNumber;
        person.ResidentCertificateNumber = string.IsNullOrWhiteSpace(person.ResidentCertificateNumber) ? null : person.ResidentCertificateNumber;

        // Validate EmployeeId uniqueness if changed
        if (!string.IsNullOrWhiteSpace(person.EmployeeId) && 
            person.EmployeeId != existingPerson.EmployeeId)
        {
            var duplicatePerson = await GetPersonByEmployeeIdAsync(person.EmployeeId);
            if (duplicatePerson != null && duplicatePerson.Id != personId)
            {
                throw new InvalidOperationException($"A person with EmployeeId '{person.EmployeeId}' already exists.");
            }
        }

        // Phase 10.6: PID Hash handling for update
        // Only validate and hash if NEW plaintext value is provided (not empty, not already hashed)
        string? newNationalIdHash = null;
        string? newPassportHash = null;
        string? newResidentCertHash = null;

        if (!string.IsNullOrWhiteSpace(person.NationalId) && !PidHasher.IsHashed(person.NationalId))
        {
            if (!IdentityDocumentValidator.IsValidTaiwanNationalId(person.NationalId))
            {
                throw new InvalidOperationException($"Invalid Taiwan National ID format");
            }
            newNationalIdHash = PidHasher.Hash(person.NationalId);
        }

        if (!string.IsNullOrWhiteSpace(person.PassportNumber) && !PidHasher.IsHashed(person.PassportNumber))
        {
            if (!IdentityDocumentValidator.IsValidPassportNumber(person.PassportNumber))
            {
                throw new InvalidOperationException($"Invalid passport number format");
            }
            newPassportHash = PidHasher.Hash(person.PassportNumber);
        }

        if (!string.IsNullOrWhiteSpace(person.ResidentCertificateNumber) && !PidHasher.IsHashed(person.ResidentCertificateNumber))
        {
            if (!IdentityDocumentValidator.IsValidResidentCertificateNumber(person.ResidentCertificateNumber))
            {
                throw new InvalidOperationException($"Invalid resident certificate format");
            }
            newResidentCertHash = PidHasher.Hash(person.ResidentCertificateNumber);
        }

        // Determine final values: use new hash if provided, otherwise keep existing
        var finalNationalId = newNationalIdHash ?? existingPerson.NationalId;
        var finalPassport = newPassportHash ?? existingPerson.PassportNumber;
        var finalResidentCert = newResidentCertHash ?? existingPerson.ResidentCertificateNumber;

        // Check for duplicate identity documents (excluding current person)
        var (isUnique, errorMessage) = await CheckPersonUniquenessAsync(
            finalNationalId,
            finalPassport,
            finalResidentCert,
            personId);

        if (!isUnique)
        {
            throw new InvalidOperationException(errorMessage);
        }

        // Detect if identity documents actually changed
        bool identityChanged = 
            (newNationalIdHash != null && newNationalIdHash != existingPerson.NationalId) ||
            (newPassportHash != null && newPassportHash != existingPerson.PassportNumber) ||
            (newResidentCertHash != null && newResidentCertHash != existingPerson.ResidentCertificateNumber);

        // Update fields
        // Contact Information
        existingPerson.Email = person.Email;
        existingPerson.PhoneNumber = person.PhoneNumber;
        // Name Information
        existingPerson.FirstName = person.FirstName;
        existingPerson.MiddleName = person.MiddleName;
        existingPerson.LastName = person.LastName;
        existingPerson.Nickname = person.Nickname;
        // Employment Information
        existingPerson.EmployeeId = person.EmployeeId;
        existingPerson.Department = person.Department;
        existingPerson.JobTitle = person.JobTitle;
        // Profile Information
        existingPerson.ProfileUrl = person.ProfileUrl;
        existingPerson.PictureUrl = person.PictureUrl;
        existingPerson.Website = person.Website;
        existingPerson.Address = person.Address;
        existingPerson.Birthdate = person.Birthdate;
        existingPerson.Gender = person.Gender;
        existingPerson.TimeZone = person.TimeZone;
        existingPerson.Locale = person.Locale;

        // Phase 10.6: Update identity fields (only if new value was provided)
        if (newNationalIdHash != null)
            existingPerson.NationalId = newNationalIdHash;
        if (newPassportHash != null)
            existingPerson.PassportNumber = newPassportHash;
        if (newResidentCertHash != null)
            existingPerson.ResidentCertificateNumber = newResidentCertHash;
        existingPerson.IdentityDocumentType = person.IdentityDocumentType;

        // Reset verification if identity document changed
        if (identityChanged)
        {
            existingPerson.IdentityVerifiedAt = null;
            existingPerson.IdentityVerifiedBy = null;
            LogIdentityDocumentChanged(personId);
        }

        // Update audit fields
        existingPerson.ModifiedAt = DateTime.UtcNow;
        existingPerson.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        LogPersonUpdated(personId, existingPerson.EmployeeId ?? "N/A");

        // Phase 10.5: Audit the update
        var auditDetails = JsonSerializer.Serialize(new
        {
            PersonId = existingPerson.Id,
            FirstName = existingPerson.FirstName,
            LastName = existingPerson.LastName,
            EmployeeId = existingPerson.EmployeeId,
            Department = existingPerson.Department,
            ModifiedBy = modifiedBy
        });
        await _auditService.LogEventAsync(
            "PersonUpdated",
            modifiedBy?.ToString(),
            auditDetails,
            null,
            null);

        return existingPerson;
    }

    public async Task<bool> DeletePersonAsync(Guid personId)
    {
        var person = await _context.Persons.FindAsync(personId);
        if (person == null)
            return false;

        // Note: Related ApplicationUsers will have their PersonId set to NULL due to OnDelete: SetNull
        _context.Persons.Remove(person);
        await _context.SaveChangesAsync(CancellationToken.None);

        LogPersonDeleted(personId, person.EmployeeId ?? "N/A");

        // Phase 10.5: Audit the deletion
        var auditDetails = JsonSerializer.Serialize(new
        {
            PersonId = person.Id,
            FirstName = person.FirstName,
            LastName = person.LastName,
            EmployeeId = person.EmployeeId,
            Department = person.Department
        });
        await _auditService.LogEventAsync(
            "PersonDeleted",
            null, // Deletion typically done by admin, tracked at controller level
            auditDetails,
            null,
            null);

        return true;
    }

    public async Task<List<ApplicationUser>> GetPersonAccountsAsync(Guid personId)
    {
        return await _context.Users
            .Where(u => u.PersonId == personId)
            .OrderBy(u => u.UserName)
            .ToListAsync();
    }

    public async Task<bool> LinkAccountToPersonAsync(Guid personId, Guid userId, Guid? modifiedBy = null)
    {
        // Verify person exists
        var person = await GetPersonByIdAsync(personId);
        if (person == null)
        {
            LogPersonNotFoundForLinking(personId);
            return false;
        }

        // Verify user exists and fetch fresh data from database (avoid cache)
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            LogUserNotFoundForLinking(userId);
            return false;
        }

        // Check if user is already linked to another person
        if (user.PersonId.HasValue && user.PersonId.Value != personId)
        {
            LogUserAlreadyLinkedToOtherPerson(userId, user.PersonId.Value);
            throw new InvalidOperationException($"User is already linked to another person (PersonId: {user.PersonId.Value})");
        }

        // Check if user is already linked to the same person (idempotent operation)
        if (user.PersonId == personId)
        {
            LogUserAlreadyLinkedToSamePerson(userId, personId);
            return true;
        }

        // Link user to person
        user.PersonId = personId;
        user.ModifiedAt = DateTime.UtcNow;
        user.ModifiedBy = modifiedBy;

        // Sync roles from existing accounts in the same Person
        var existingAccounts = await _context.Users
            .Where(u => u.PersonId == personId && u.Id != userId)
            .ToListAsync();
            
        if (existingAccounts.Any())
        {
            // Get roles from the first existing account to replicate
            var sourceAccount = existingAccounts.First();
            var sourceRoles = await _userManager.GetRolesAsync(sourceAccount);
            if (sourceRoles.Any())
            {
                var currentUserRoles = await _userManager.GetRolesAsync(user);
                var rolesToAdd = sourceRoles.Except(currentUserRoles).ToList();
                if (rolesToAdd.Any())
                {
                    await _userManager.AddToRolesAsync(user, rolesToAdd);
                    LogRolesSynced(rolesToAdd.Count, userId, string.Join(", ", rolesToAdd));
                }
            }
        }

        await _context.SaveChangesAsync(CancellationToken.None);

        LogPersonAccountLinked(userId, user.UserName, personId, person.EmployeeId ?? "N/A");

        // Phase 10.5: Audit the account linking
        var auditDetails = JsonSerializer.Serialize(new
        {
            PersonId = personId,
            ApplicationUserId = userId,
            UserName = user.UserName,
            Email = user.Email,
            PersonEmployeeId = person.EmployeeId,
            LinkedBy = modifiedBy
        });
        await _auditService.LogEventAsync(
            "PersonAccountLinked",
            modifiedBy?.ToString(),
            auditDetails,
            null,
            null);

        return true;
    }

    public async Task<bool> UnlinkAccountFromPersonAsync(Guid userId, Guid? modifiedBy = null)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            LogUserNotFoundForUnlinking(userId);
            return false;
        }

        var previousPersonId = user.PersonId;

        // Unlink user from person
        user.PersonId = null;
        user.ModifiedAt = DateTime.UtcNow;
        user.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        LogPersonAccountUnlinked(userId, user.UserName, previousPersonId ?? Guid.Empty);

        // Phase 10.5: Audit the account unlinking
        var auditDetails = JsonSerializer.Serialize(new
        {
            PersonId = previousPersonId,
            ApplicationUserId = userId,
            UserName = user.UserName,
            Email = user.Email,
            UnlinkedBy = modifiedBy
        });
        await _auditService.LogEventAsync(
            "PersonAccountUnlinked",
            modifiedBy?.ToString(),
            auditDetails,
            null,
            null);

        return true;
    }

    public async Task<List<Person>> SearchPersonsAsync(string searchTerm, int skip = 0, int take = 50)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return await GetAllPersonsAsync(skip, take);

        var term = searchTerm.ToLower();

        return await _context.Persons
            .Include(p => p.Accounts)
            .Where(p => 
                (p.FirstName != null && p.FirstName.ToLower().Contains(term)) ||
                (p.LastName != null && p.LastName.ToLower().Contains(term)) ||
                (p.EmployeeId != null && p.EmployeeId.ToLower().Contains(term)) ||
                (p.Nickname != null && p.Nickname.ToLower().Contains(term)))
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetPersonsCountAsync()
    {
        return await _context.Persons.CountAsync();
    }

    /// <inheritdoc />
    public async Task<List<ApplicationUser>> GetUnlinkedUsersAsync(string? searchTerm = null)
    {
        LogGettingUnlinkedUsers(searchTerm ?? "(none)");

        var query = _context.Users
            .Where(u => u.PersonId == null);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.UserName != null && u.UserName.ToLower().Contains(term)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(u => u.Email)
            .Take(100) // Limit to prevent large result sets
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<(bool success, string? errorMessage)> CheckPersonUniquenessAsync(
        string? nationalId,
        string? passportNumber,
        string? residentCertificateNumber,
        Guid? excludePersonId = null)
    {
        // Normalize empty strings to null for proper comparison
        nationalId = string.IsNullOrWhiteSpace(nationalId) ? null : nationalId;
        passportNumber = string.IsNullOrWhiteSpace(passportNumber) ? null : passportNumber;
        residentCertificateNumber = string.IsNullOrWhiteSpace(residentCertificateNumber) ? null : residentCertificateNumber;



        // If no identity documents provided, skip uniqueness check
        if (nationalId == null && passportNumber == null && residentCertificateNumber == null)
        {
            return (true, null);
        }

        // Check if any identity document already exists
        var existingPerson = await _context.Persons
            .Where(p => excludePersonId == null || p.Id != excludePersonId)
            .Where(p =>
                (nationalId != null && p.NationalId == nationalId) ||
                (passportNumber != null && p.PassportNumber == passportNumber) ||
                (residentCertificateNumber != null && p.ResidentCertificateNumber == residentCertificateNumber))
            .FirstOrDefaultAsync();

        if (existingPerson != null)
        {
            return (false, $"Person with this identity document already exists (PersonId: {existingPerson.Id})");
        }

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyPersonIdentityAsync(Guid personId, Guid verifiedByUserId)
    {
        var person = await GetPersonByIdAsync(personId);
        if (person == null)
        {
            LogPersonNotFoundForVerification(personId);
            return false;
        }

        // Check if any identity document is provided
        if (string.IsNullOrWhiteSpace(person.NationalId) &&
            string.IsNullOrWhiteSpace(person.PassportNumber) &&
            string.IsNullOrWhiteSpace(person.ResidentCertificateNumber))
        {
            LogPersonHasNoIdentityDocument(personId);
            return false;
        }

        // Set verification fields
        person.IdentityVerifiedAt = DateTime.UtcNow;
        person.IdentityVerifiedBy = verifiedByUserId;

        await _context.SaveChangesAsync(CancellationToken.None);

        LogPersonIdentityVerified(personId, verifiedByUserId);

        // Audit the identity verification
        var auditDetails = JsonSerializer.Serialize(new
        {
            PersonId = personId,
            VerifiedBy = verifiedByUserId,
            VerifiedAt = person.IdentityVerifiedAt,
            NationalId = !string.IsNullOrWhiteSpace(person.NationalId) ? "***" : null,
            PassportNumber = !string.IsNullOrWhiteSpace(person.PassportNumber) ? "***" : null,
            ResidentCertificate = !string.IsNullOrWhiteSpace(person.ResidentCertificateNumber) ? "***" : null
        });
        await _auditService.LogEventAsync(
            "PersonIdentityVerified",
            verifiedByUserId.ToString(),
            auditDetails,
            null,
            null);

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created new person {PersonId} (EmployeeId: {EmployeeId})")]
    partial void LogPersonCreated(Guid personId, string employeeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity document changed for person {PersonId}, reset verification status")]
    partial void LogIdentityDocumentChanged(Guid personId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated person {PersonId} (EmployeeId: {EmployeeId})")]
    partial void LogPersonUpdated(Guid personId, string employeeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted person {PersonId} (EmployeeId: {EmployeeId})")]
    partial void LogPersonDeleted(Guid personId, string employeeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot link account: Person {PersonId} not found")]
    partial void LogPersonNotFoundForLinking(Guid personId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot link account: User {UserId} not found")]
    partial void LogUserNotFoundForLinking(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot link account: User {UserId} is already linked to person {ExistingPersonId}")]
    partial void LogUserAlreadyLinkedToOtherPerson(Guid userId, Guid existingPersonId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} is already linked to person {PersonId}, skipping")]
    partial void LogUserAlreadyLinkedToSamePerson(Guid userId, Guid personId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synced {RoleCount} roles from existing Person accounts to user {UserId}: {Roles}")]
    partial void LogRolesSynced(int roleCount, Guid userId, string roles);

    [LoggerMessage(Level = LogLevel.Information, Message = "Linked user {UserId} ({UserName}) to person {PersonId} (EmployeeId: {EmployeeId})")]
    partial void LogPersonAccountLinked(Guid userId, string? userName, Guid personId, string employeeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot unlink account: User {UserId} not found")]
    partial void LogUserNotFoundForUnlinking(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unlinked user {UserId} ({UserName}) from person {PersonId}")]
    partial void LogPersonAccountUnlinked(Guid userId, string? userName, Guid personId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting unlinked users with search term: {SearchTerm}")]
    partial void LogGettingUnlinkedUsers(string searchTerm);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot verify identity: Person {PersonId} not found")]
    partial void LogPersonNotFoundForVerification(Guid personId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot verify identity: Person {PersonId} has no identity document")]
    partial void LogPersonHasNoIdentityDocument(Guid personId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Verified identity for person {PersonId} by user {VerifiedBy}")]
    partial void LogPersonIdentityVerified(Guid personId, Guid verifiedBy);
}
