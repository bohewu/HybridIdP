using Core.Domain;
using Core.Domain.Entities;
using System.Threading;

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
    Task<Person?> GetPersonByIdAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a person by their employee ID
    /// </summary>
    /// <param name="employeeId">The employee ID</param>
    /// <returns>The person entity if found, null otherwise</returns>
    Task<Person?> GetPersonByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers all resources (Scopes, Clients, API Resources) owned by one person to another.
    /// Used when offboarding users to prevent orphaned resources.
    /// </summary>
    Task TransferAssetsAsync(Guid fromPersonId, Guid toPersonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all persons with optional pagination
    /// </summary>
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <returns>List of persons</returns>
    Task<List<Person>> GetAllPersonsAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new person
    /// </summary>
    /// <param name="person">The person entity to create</param>
    /// <param name="createdBy">User ID of the creator</param>
    /// <returns>The created person entity</returns>
    Task<Person> CreatePersonAsync(Person person, Guid? createdBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="person">The updated person data</param>
    /// <param name="modifiedBy">User ID of the modifier</param>
    /// <returns>The updated person entity if found, null otherwise</returns>
    Task<Person?> UpdatePersonAsync(Guid personId, Person person, Guid? modifiedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <returns>True if deleted successfully, false if not found</returns>
    Task<bool> DeletePersonAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all accounts (ApplicationUsers) linked to a person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <returns>List of linked application users</returns>
    Task<List<ApplicationUser>> GetPersonAccountsAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Link an existing ApplicationUser to a Person
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="userId">The user's unique ID</param>
    /// <param name="modifiedBy">User ID of the modifier</param>
    /// <returns>True if linked successfully, false if person or user not found</returns>
    Task<bool> LinkAccountToPersonAsync(Guid personId, Guid userId, Guid? modifiedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlink an ApplicationUser from their Person
    /// </summary>
    /// <param name="userId">The user's unique ID</param>
    /// <param name="modifiedBy">User ID of the modifier</param>
    /// <returns>True if unlinked successfully, false if user not found</returns>
    Task<bool> UnlinkAccountFromPersonAsync(Guid userId, Guid? modifiedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search persons by name or employee ID
    /// </summary>
    /// <param name="searchTerm">Search term to match against name or employee ID</param>
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <returns>List of matching persons</returns>
    Task<List<Person>> SearchPersonsAsync(string searchTerm, int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the total count of persons
    /// </summary>
    /// <returns>Total number of persons</returns>
    Task<int> GetPersonsCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all users that are not linked to any person (PersonId is null)
    /// </summary>
    /// <param name="searchTerm">Optional search term to filter by email or username</param>
    /// <returns>List of unlinked application users</returns>
    Task<List<ApplicationUser>> GetUnlinkedUsersAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a person with the given identity documents already exists (Phase 10.6)
    /// </summary>
    /// <param name="nationalId">National ID to check</param>
    /// <param name="passportNumber">Passport number to check</param>
    /// <param name="residentCertificateNumber">Resident certificate number to check</param>
    /// <param name="excludePersonId">Person ID to exclude from the check (for updates)</param>
    /// <returns>Tuple with success flag and error message if duplicate found</returns>
    Task<(bool success, string? errorMessage)> CheckPersonUniquenessAsync(
        string? nationalId,
        string? passportNumber,
        string? residentCertificateNumber,
        Guid? excludePersonId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a person's identity document (Phase 10.6)
    /// </summary>
    /// <param name="personId">The person's unique ID</param>
    /// <param name="verifiedByUserId">The user ID who verified the identity</param>
    /// <returns>True if verification successful, false if person not found or identity document missing</returns>
    Task<bool> VerifyPersonIdentityAsync(Guid personId, Guid verifiedByUserId, CancellationToken cancellationToken = default);
}
