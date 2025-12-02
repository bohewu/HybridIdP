using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service implementation for managing Person entities and their relationships with ApplicationUsers.
/// Phase 10.2: Person Service & API
/// Phase 10.5: Added audit trail for all CRUD operations
/// Phase 10.6: Added identity document validation and verification
/// </summary>
public class PersonService : IPersonService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PersonService> _logger;
    private readonly IAuditService _auditService;

    public PersonService(
        IApplicationDbContext context,
        ILogger<PersonService> logger,
        IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
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

        // Validate EmployeeId uniqueness if provided
        if (!string.IsNullOrWhiteSpace(person.EmployeeId))
        {
            var existingPerson = await GetPersonByEmployeeIdAsync(person.EmployeeId);
            if (existingPerson != null)
            {
                throw new InvalidOperationException($"A person with EmployeeId '{person.EmployeeId}' already exists.");
            }
        }

        // Phase 10.6: Validate identity documents
        if (!string.IsNullOrWhiteSpace(person.NationalId))
        {
            if (!IdentityDocumentValidator.IsValidTaiwanNationalId(person.NationalId))
            {
                throw new InvalidOperationException($"Invalid Taiwan National ID format: {person.NationalId}");
            }
        }

        if (!string.IsNullOrWhiteSpace(person.PassportNumber))
        {
            if (!IdentityDocumentValidator.IsValidPassportNumber(person.PassportNumber))
            {
                throw new InvalidOperationException($"Invalid passport number format: {person.PassportNumber}");
            }
        }

        if (!string.IsNullOrWhiteSpace(person.ResidentCertificateNumber))
        {
            if (!IdentityDocumentValidator.IsValidResidentCertificateNumber(person.ResidentCertificateNumber))
            {
                throw new InvalidOperationException($"Invalid resident certificate format: {person.ResidentCertificateNumber}");
            }
        }

        // Phase 10.6: Check for duplicate identity documents
        var (isUnique, errorMessage) = await CheckPersonUniquenessAsync(
            person.NationalId,
            person.PassportNumber,
            person.ResidentCertificateNumber,
            null);

        if (!isUnique)
        {
            throw new InvalidOperationException(errorMessage);
        }

        _context.Persons.Add(person);
        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Created new person {PersonId} (EmployeeId: {EmployeeId})", 
            person.Id, person.EmployeeId ?? "N/A");

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

        // Phase 10.6: Validate identity documents
        if (!string.IsNullOrWhiteSpace(person.NationalId))
        {
            if (!IdentityDocumentValidator.IsValidTaiwanNationalId(person.NationalId))
            {
                throw new InvalidOperationException($"Invalid Taiwan National ID format: {person.NationalId}");
            }
        }

        if (!string.IsNullOrWhiteSpace(person.PassportNumber))
        {
            if (!IdentityDocumentValidator.IsValidPassportNumber(person.PassportNumber))
            {
                throw new InvalidOperationException($"Invalid passport number format: {person.PassportNumber}");
            }
        }

        if (!string.IsNullOrWhiteSpace(person.ResidentCertificateNumber))
        {
            if (!IdentityDocumentValidator.IsValidResidentCertificateNumber(person.ResidentCertificateNumber))
            {
                throw new InvalidOperationException($"Invalid resident certificate format: {person.ResidentCertificateNumber}");
            }
        }

        // Phase 10.6: Check for duplicate identity documents (excluding current person)
        var (isUnique, errorMessage) = await CheckPersonUniquenessAsync(
            person.NationalId,
            person.PassportNumber,
            person.ResidentCertificateNumber,
            personId);

        if (!isUnique)
        {
            throw new InvalidOperationException(errorMessage);
        }

        // Phase 10.6: Reset verification fields if identity document changes
        bool identityChanged = 
            person.NationalId != existingPerson.NationalId ||
            person.PassportNumber != existingPerson.PassportNumber ||
            person.ResidentCertificateNumber != existingPerson.ResidentCertificateNumber;

        // Update fields
        existingPerson.FirstName = person.FirstName;
        existingPerson.MiddleName = person.MiddleName;
        existingPerson.LastName = person.LastName;
        existingPerson.Nickname = person.Nickname;
        existingPerson.EmployeeId = person.EmployeeId;
        existingPerson.Department = person.Department;
        existingPerson.JobTitle = person.JobTitle;
        existingPerson.ProfileUrl = person.ProfileUrl;
        existingPerson.PictureUrl = person.PictureUrl;
        existingPerson.Website = person.Website;
        existingPerson.Address = person.Address;
        existingPerson.Birthdate = person.Birthdate;
        existingPerson.Gender = person.Gender;
        existingPerson.TimeZone = person.TimeZone;
        existingPerson.Locale = person.Locale;

        // Phase 10.6: Update identity fields
        existingPerson.NationalId = person.NationalId;
        existingPerson.PassportNumber = person.PassportNumber;
        existingPerson.ResidentCertificateNumber = person.ResidentCertificateNumber;
        existingPerson.IdentityDocumentType = person.IdentityDocumentType;

        // Reset verification if identity document changed
        if (identityChanged)
        {
            existingPerson.IdentityVerifiedAt = null;
            existingPerson.IdentityVerifiedBy = null;
            _logger.LogInformation("Identity document changed for person {PersonId}, reset verification status", personId);
        }

        // Update audit fields
        existingPerson.ModifiedAt = DateTime.UtcNow;
        existingPerson.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Updated person {PersonId} (EmployeeId: {EmployeeId})", 
            personId, existingPerson.EmployeeId ?? "N/A");

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

        _logger.LogInformation("Deleted person {PersonId} (EmployeeId: {EmployeeId})", 
            personId, person.EmployeeId ?? "N/A");

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
            _logger.LogWarning("Cannot link account: Person {PersonId} not found", personId);
            return false;
        }

        // Verify user exists and fetch fresh data from database (avoid cache)
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            _logger.LogWarning("Cannot link account: User {UserId} not found", userId);
            return false;
        }

        // Check if user is already linked to another person
        if (user.PersonId.HasValue && user.PersonId.Value != personId)
        {
            _logger.LogWarning("Cannot link account: User {UserId} is already linked to person {ExistingPersonId}", 
                userId, user.PersonId.Value);
            throw new InvalidOperationException($"User is already linked to another person (PersonId: {user.PersonId.Value})");
        }

        // Check if user is already linked to the same person (idempotent operation)
        if (user.PersonId == personId)
        {
            _logger.LogInformation("User {UserId} is already linked to person {PersonId}, skipping", userId, personId);
            return true;
        }

        // Link user to person
        user.PersonId = personId;
        user.ModifiedAt = DateTime.UtcNow;
        user.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Linked user {UserId} ({UserName}) to person {PersonId} (EmployeeId: {EmployeeId})", 
            userId, user.UserName, personId, person.EmployeeId ?? "N/A");

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
            _logger.LogWarning("Cannot unlink account: User {UserId} not found", userId);
            return false;
        }

        var previousPersonId = user.PersonId;

        // Unlink user from person
        user.PersonId = null;
        user.ModifiedAt = DateTime.UtcNow;
        user.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Unlinked user {UserId} ({UserName}) from person {PersonId}", 
            userId, user.UserName, previousPersonId ?? Guid.Empty);

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
        _logger.LogInformation("Getting unlinked users with search term: {SearchTerm}", searchTerm ?? "(none)");

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
            _logger.LogWarning("Cannot verify identity: Person {PersonId} not found", personId);
            return false;
        }

        // Check if any identity document is provided
        if (string.IsNullOrWhiteSpace(person.NationalId) &&
            string.IsNullOrWhiteSpace(person.PassportNumber) &&
            string.IsNullOrWhiteSpace(person.ResidentCertificateNumber))
        {
            _logger.LogWarning("Cannot verify identity: Person {PersonId} has no identity document", personId);
            return false;
        }

        // Set verification fields
        person.IdentityVerifiedAt = DateTime.UtcNow;
        person.IdentityVerifiedBy = verifiedByUserId;

        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Verified identity for person {PersonId} by user {VerifiedBy}", 
            personId, verifiedByUserId);

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
}
