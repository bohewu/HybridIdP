using Core.Domain;
using Core.Domain.Entities;

namespace Core.Application;

/// <summary>
/// Service interface for managing Person entities and their relationships with ApplicationUsers.
/// Phase 10.2: Person Service & API
/// </summary>
public interface IPersonService
{
    /// <summary>
    /// Get a person by their unique identifier
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <returns>The person entity if found, null otherwise</returns>
    Task<Person?> GetPersonByIdAsync(Guid personId);

    /// <summary>
    /// Get a person by their employee ID
    /// </summary>
    /// <param name="employeeId">The employee ID</param>
    /// <returns>The person entity if found, null otherwise</returns>
    Task<Person?> GetPersonByEmployeeIdAsync(string employeeId);

    /// <summary>
    /// Get all persons with optional pagination
    /// </summary>
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <returns>List of persons</returns>
    Task<List<Person>> GetAllPersonsAsync(int skip = 0, int take = 50);

    /// <summary>
    /// Create a new person
    /// </summary>
    /// <param name="person">The person entity to create</param>
    /// <param name="createdBy">User ID of the creator</param>
    /// <returns>The created person entity</returns>
    Task<Person> CreatePersonAsync(Person person, Guid? createdBy = null);

    /// <summary>
    /// Update an existing person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="person">The updated person data</param>
    /// <param name="modifiedBy">User ID of the modifier</param>
    /// <returns>The updated person entity if found, null otherwise</returns>
    Task<Person?> UpdatePersonAsync(Guid personId, Person person, Guid? modifiedBy = null);

    /// <summary>
    /// Delete a person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <returns>True if deleted successfully, false if not found</returns>
    Task<bool> DeletePersonAsync(Guid personId);

    /// <summary>
    /// Get all accounts (ApplicationUsers) linked to a person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <returns>List of linked application users</returns>
    Task<List<ApplicationUser>> GetPersonAccountsAsync(Guid personId);

    /// <summary>
    /// Link an existing ApplicationUser to a Person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="userId">The user's unique ID</param>
    /// <param name="modifiedBy">User ID of the modifier</param>
    /// <returns>True if linked successfully, false if person or user not found</returns>
    Task<bool> LinkAccountToPersonAsync(Guid personId, Guid userId, Guid? modifiedBy = null);

    /// <summary>
    /// Unlink an ApplicationUser from their Person
    /// </summary>
    /// <param name="userId">The user's unique ID</param>
    /// <param name="modifiedBy">User ID of the modifier</param>
    /// <returns>True if unlinked successfully, false if user not found</returns>
    Task<bool> UnlinkAccountFromPersonAsync(Guid userId, Guid? modifiedBy = null);

    /// <summary>
    /// Search persons by name or employee ID
    /// </summary>
    /// <param name="searchTerm">Search term to match against name or employee ID</param>
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <returns>List of matching persons</returns>
    Task<List<Person>> SearchPersonsAsync(string searchTerm, int skip = 0, int take = 50);

    /// <summary>
    /// Get the total count of persons
    /// </summary>
    /// <returns>Total number of persons</returns>
    Task<int> GetPersonsCountAsync();
}
