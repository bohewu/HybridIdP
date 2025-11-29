using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service implementation for managing Person entities and their relationships with ApplicationUsers.
/// Phase 10.2: Person Service & API
/// </summary>
public class PersonService : IPersonService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PersonService> _logger;

    public PersonService(
        IApplicationDbContext context,
        ILogger<PersonService> logger)
    {
        _context = context;
        _logger = logger;
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

        _context.Persons.Add(person);
        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Created new person {PersonId} (EmployeeId: {EmployeeId})", 
            person.Id, person.EmployeeId ?? "N/A");

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

        // Update audit fields
        existingPerson.ModifiedAt = DateTime.UtcNow;
        existingPerson.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Updated person {PersonId} (EmployeeId: {EmployeeId})", 
            personId, existingPerson.EmployeeId ?? "N/A");

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

        // Verify user exists
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("Cannot link account: User {UserId} not found", userId);
            return false;
        }

        // Link user to person
        user.PersonId = personId;
        user.ModifiedAt = DateTime.UtcNow;
        user.ModifiedBy = modifiedBy;

        await _context.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Linked user {UserId} ({UserName}) to person {PersonId} (EmployeeId: {EmployeeId})", 
            userId, user.UserName, personId, person.EmployeeId ?? "N/A");

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

        return true;
    }

    public async Task<List<Person>> SearchPersonsAsync(string searchTerm, int skip = 0, int take = 50)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return await GetAllPersonsAsync(skip, take);

        var term = searchTerm.ToLower();

        return await _context.Persons
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
}
