using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Admin;

/// <summary>
/// Admin API controller for managing Persons
/// Phase 10.2: Person Service & API
/// Phase 10.3: Permission-based authorization
/// </summary>
[ApiController]
[Route("api/admin/people")]
[ApiAuthorize]
[ValidateCsrfForCookies]
public partial class PersonsController : ControllerBase
{
    private readonly IPersonService _personService;
    private readonly ILogger<PersonsController> _logger;
    private readonly IAuditService _auditService;
    private readonly IUserManagementService _userManagementService;

    public PersonsController(
        IPersonService personService,
        ILogger<PersonsController> logger,
        IAuditService auditService,
        IUserManagementService userManagementService)
    {
        _personService = personService;
        _logger = logger;
        _auditService = auditService;
        _userManagementService = userManagementService;
    }

    /// <summary>
    /// Get all persons with pagination
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Persons.Read)]
    public async Task<ActionResult<PersonListResponseDto>> GetPersons([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        try
        {
            var persons = await _personService.GetAllPersonsAsync(skip, take);
            var totalCount = await _personService.GetPersonsCountAsync();

            var response = new PersonListResponseDto
            {
                Persons = persons.Select(MapToResponseDto).ToList(),
                TotalCount = totalCount,
                Skip = skip,
                Take = take
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            LogErrorRetrievingPersonsList(ex);
            return StatusCode(500, "An error occurred while retrieving persons");
        }
    }

    /// <summary>
    /// Get a specific person by ID
    /// </summary>
    [HttpGet("{id}")]
    [HasPermission(Permissions.Persons.Read)]
    public async Task<ActionResult<PersonResponseDto>> GetPerson(Guid id)
    {
        try
        {
            var person = await _personService.GetPersonByIdAsync(id);
            if (person == null)
                return NotFound($"Person with ID {id} not found");

            return Ok(MapToResponseDto(person));
        }
        catch (Exception ex)
        {
            LogErrorRetrievingPerson(ex, id);
            return StatusCode(500, "An error occurred while retrieving the person");
        }
    }

    /// <summary>
    /// Search persons by name or employee ID
    /// </summary>
    [HttpGet("search")]
    [HasPermission(Permissions.Persons.Read)]
    public async Task<ActionResult<PersonListResponseDto>> SearchPersons(
        [FromQuery] string term, 
        [FromQuery] int skip = 0, 
        [FromQuery] int take = 50)
    {
        try
        {
            var persons = await _personService.SearchPersonsAsync(term, skip, take);
            var totalCount = await _personService.GetPersonsCountAsync(); // Note: This is total, not search result count

            var response = new PersonListResponseDto
            {
                Persons = persons.Select(MapToResponseDto).ToList(),
                TotalCount = totalCount,
                Skip = skip,
                Take = take
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            LogErrorSearchingPersons(ex, term?.Replace(Environment.NewLine, "") ?? string.Empty);
            return StatusCode(500, "An error occurred while searching persons");
        }
    }

    /// <summary>
    /// Create a new person
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Persons.Create)]
    public async Task<ActionResult<PersonResponseDto>> CreatePerson([FromBody] PersonDto dto)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var person = MapFromDto(dto);

            var createdPerson = await _personService.CreatePersonAsync(person, currentUserId);

            await _auditService.LogEventAsync(
                "PersonCreated",
                currentUserId?.ToString(),
                $"Created person {createdPerson.Id} (EmployeeId: {createdPerson.EmployeeId})",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString());

            return CreatedAtAction(
                nameof(GetPerson),
                new { id = createdPerson.Id },
                MapToResponseDto(createdPerson));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            LogErrorCreatingPerson(ex, ex.Message);
            return StatusCode(500, "An error occurred while creating the person");
        }
    }

    /// <summary>
    /// Update an existing person
    /// </summary>
    [HttpPut("{id}")]
    [HasPermission(Permissions.Persons.Update)]
    public async Task<ActionResult<PersonResponseDto>> UpdatePerson(Guid id, [FromBody] PersonDto dto)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var person = MapFromDto(dto);

            var updatedPerson = await _personService.UpdatePersonAsync(id, person, currentUserId);
            if (updatedPerson == null)
                return NotFound($"Person with ID {id} not found");

            await _auditService.LogEventAsync(
                "PersonUpdated",
                currentUserId?.ToString(),
                $"Updated person {id} (EmployeeId: {updatedPerson.EmployeeId})",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString());

            return Ok(MapToResponseDto(updatedPerson));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            LogErrorUpdatingPerson(ex, id);
            return StatusCode(500, "An error occurred while updating the person");
        }
    }

    /// <summary>
    /// Delete a person
    /// </summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.Persons.Delete)]
    public async Task<IActionResult> DeletePerson(Guid id)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var deleted = await _personService.DeletePersonAsync(id);

            if (!deleted)
                return NotFound($"Person with ID {id} not found");

            await _auditService.LogEventAsync(
                "PersonDeleted",
                currentUserId?.ToString(),
                $"Deleted person {id}",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString());

            return NoContent();
        }
        catch (Exception ex)
        {
            LogErrorDeletingPerson(ex, id);
            return StatusCode(500, "An error occurred while deleting the person");
        }
    }

    /// <summary>
    /// Get all accounts linked to a person
    /// </summary>
    [HttpGet("{id}/accounts")]
    [HasPermission(Permissions.Persons.Read)]
    public async Task<ActionResult<List<LinkedAccountDto>>> GetPersonAccounts(Guid id)
    {
        try
        {
            var person = await _personService.GetPersonByIdAsync(id);
            if (person == null)
                return NotFound($"Person with ID {id} not found");

            var accounts = await _personService.GetPersonAccountsAsync(id);
            var accountDtos = accounts.Select(a => new LinkedAccountDto
            {
                Id = a.Id,
                UserName = a.UserName,
                Email = a.Email,
                IsActive = a.IsActive,
                LastLoginDate = a.LastLoginDate
            }).ToList();

            return Ok(accountDtos);
        }
        catch (Exception ex)
        {
            LogErrorRetrievingAccounts(ex, id);
            return StatusCode(500, "An error occurred while retrieving accounts");
        }
    }

    /// <summary>
    /// Link an account to a person
    /// </summary>
    [HttpPost("{id}/accounts")]
    [HasPermission(Permissions.Persons.Update)]
    public async Task<IActionResult> LinkAccount(Guid id, [FromBody] LinkAccountDto dto)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var linked = await _personService.LinkAccountToPersonAsync(id, dto.UserId, currentUserId);

            if (!linked)
                return NotFound("Person or user not found");

            await _auditService.LogEventAsync(
                "AccountLinkedToPerson",
                currentUserId?.ToString(),
                $"Linked user {dto.UserId} to person {id}",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString());

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            LogWarningInvalidLinkOperation(ex, dto.UserId, id);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            LogErrorLinkingAccount(ex, dto.UserId, id);
            return StatusCode(500, "An error occurred while linking the account");
        }
    }

    /// <summary>
    /// Unlink an account from its person
    /// </summary>
    [HttpDelete("accounts/{userId}")]
    [HasPermission(Permissions.Persons.Update)]
    public async Task<IActionResult> UnlinkAccount(Guid userId)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var unlinked = await _personService.UnlinkAccountFromPersonAsync(userId, currentUserId);

            if (!unlinked)
                return NotFound($"User with ID {userId} not found");

            await _auditService.LogEventAsync(
                "AccountUnlinkedFromPerson",
                currentUserId?.ToString(),
                $"Unlinked user {userId} from their person",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString());

            return NoContent();
        }
        catch (Exception ex)
        {
            LogErrorUnlinkingAccount(ex, userId);
            return StatusCode(500, "An error occurred while unlinking the account");
        }
    }

    /// <summary>
    /// Get all users that are not linked to any person (available for linking)
    /// </summary>
    [HttpGet("available-users")]
    [HasPermission(Permissions.Persons.Read)]
    public async Task<ActionResult<List<LinkedAccountDto>>> GetAvailableUsers([FromQuery] string? search = null)
    {
        try
        {
            var availableUsers = await _personService.GetUnlinkedUsersAsync(search);
            var accountDtos = availableUsers.Select(a => new LinkedAccountDto
            {
                Id = a.Id,
                UserName = a.UserName,
                Email = a.Email,
                IsActive = a.IsActive,
                LastLoginDate = a.LastLoginDate
            }).ToList();

            return Ok(accountDtos);
        }
        catch (Exception ex)
        {
            LogErrorRetrievingAvailableUsers(ex);
            return StatusCode(500, "An error occurred while retrieving available users");
        }
    }

    /// <summary>
    /// Verify a person's identity document (Phase 10.6)
    /// </summary>
    [HttpPost("{id}/verify-identity")]
    [HasPermission(Permissions.Persons.Update)]
    public async Task<IActionResult> VerifyIdentity(Guid id)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null)
                return Unauthorized();

            var verified = await _personService.VerifyPersonIdentityAsync(id, currentUserId.Value);

            if (!verified)
                return BadRequest("Person not found or no identity document provided");

            await _auditService.LogEventAsync(
                "PersonIdentityVerificationRequested",
                currentUserId?.ToString(),
                $"Verified identity for person {id}",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString());

            return NoContent();
        }
        catch (Exception ex)
        {
            LogErrorVerifyingIdentity(ex, id);
            return StatusCode(500, "An error occurred while verifying the identity");
        }
    }

    #region Helper Methods

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private Person MapFromDto(PersonDto dto)
    {
        return new Person
        {
            // Contact Information
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            // Name Information
            FirstName = dto.FirstName,
            MiddleName = dto.MiddleName,
            LastName = dto.LastName,
            Nickname = dto.Nickname,
            // Employment Information
            EmployeeId = dto.EmployeeId,
            Department = dto.Department,
            JobTitle = dto.JobTitle,
            // Profile Information
            ProfileUrl = dto.ProfileUrl,
            PictureUrl = dto.PictureUrl,
            Website = dto.Website,
            Address = dto.Address,
            Birthdate = dto.Birthdate,
            Gender = dto.Gender,
            TimeZone = dto.TimeZone,
            Locale = dto.Locale,
            // Phase 10.6: Identity fields
            NationalId = dto.NationalId,
            PassportNumber = dto.PassportNumber,
            ResidentCertificateNumber = dto.ResidentCertificateNumber,
            IdentityDocumentType = dto.IdentityDocumentType,
            // Phase 18: Lifecycle fields
            Status = Enum.TryParse<PersonStatus>(dto.Status, true, out var status) ? status : PersonStatus.Active,
            StartDate = dto.StartDate.HasValue ? DateTime.SpecifyKind(dto.StartDate.Value.Date, DateTimeKind.Utc) : null,
            EndDate = dto.EndDate.HasValue ? DateTime.SpecifyKind(dto.EndDate.Value.Date, DateTimeKind.Utc) : null
        };
    }

    private PersonResponseDto MapToResponseDto(Person person)
    {
        return new PersonResponseDto
        {
            Id = person.Id,
            // Contact Information
            Email = person.Email,
            PhoneNumber = person.PhoneNumber,
            // Name Information
            FirstName = person.FirstName,
            MiddleName = person.MiddleName,
            LastName = person.LastName,
            Nickname = person.Nickname,
            // Employment Information
            EmployeeId = person.EmployeeId,
            Department = person.Department,
            JobTitle = person.JobTitle,
            // Profile Information
            ProfileUrl = person.ProfileUrl,
            PictureUrl = person.PictureUrl,
            Website = person.Website,
            Address = person.Address,
            Birthdate = person.Birthdate,
            Gender = person.Gender,
            TimeZone = person.TimeZone,
            Locale = person.Locale,
            CreatedAt = person.CreatedAt,
            CreatedBy = person.CreatedBy,
            ModifiedAt = person.ModifiedAt,
            ModifiedBy = person.ModifiedBy,
            // Phase 10.6: Identity fields - Return masked indicators instead of hashed values
            // Users can see if a value exists without exposing the hash
            NationalId = !string.IsNullOrWhiteSpace(person.NationalId) ? "●●●●●●●●●●" : null,
            PassportNumber = !string.IsNullOrWhiteSpace(person.PassportNumber) ? "●●●●●●●●●●" : null,
            ResidentCertificateNumber = !string.IsNullOrWhiteSpace(person.ResidentCertificateNumber) ? "●●●●●●●●●●" : null,
            IdentityDocumentType = person.IdentityDocumentType,
            IdentityVerifiedAt = person.IdentityVerifiedAt,
            IdentityVerifiedBy = person.IdentityVerifiedBy,
            // Phase 18: Lifecycle fields
            Status = person.Status.ToString(),
            StatusDisplayName = person.Status.ToString(),
            StartDate = person.StartDate,
            EndDate = person.EndDate,
            IsDeleted = person.IsDeleted,
            DeletedAt = person.DeletedAt,
            DeletedBy = person.DeletedBy,
            CanAuthenticate = person.CanAuthenticate(),
            Accounts = person.Accounts?.Select(a => new LinkedAccountDto
            {
                Id = a.Id,
                UserName = a.UserName,
                Email = a.Email,
                IsActive = a.IsActive,
                LastLoginDate = a.LastLoginDate
            }).ToList()
        };
    }

    #endregion

    #region LoggerMessage Methods

    [LoggerMessage(Level = LogLevel.Error, Message = "Error retrieving persons list")]
    partial void LogErrorRetrievingPersonsList(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error retrieving person {PersonId}")]
    partial void LogErrorRetrievingPerson(Exception ex, Guid personId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error searching persons with term: {SearchTerm}")]
    partial void LogErrorSearchingPersons(Exception ex, string searchTerm);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating person: {Message}")]
    partial void LogErrorCreatingPerson(Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error updating person {PersonId}")]
    partial void LogErrorUpdatingPerson(Exception ex, Guid personId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error deleting person {PersonId}")]
    partial void LogErrorDeletingPerson(Exception ex, Guid personId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error retrieving accounts for person {PersonId}")]
    partial void LogErrorRetrievingAccounts(Exception ex, Guid personId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid operation when linking account {UserId} to person {PersonId}")]
    partial void LogWarningInvalidLinkOperation(Exception ex, Guid userId, Guid personId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error linking account {UserId} to person {PersonId}")]
    partial void LogErrorLinkingAccount(Exception ex, Guid userId, Guid personId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error unlinking account {UserId}")]
    partial void LogErrorUnlinkingAccount(Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error retrieving available users")]
    partial void LogErrorRetrievingAvailableUsers(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error verifying identity for person {PersonId}")]
    partial void LogErrorVerifyingIdentity(Exception ex, Guid personId);

    #endregion
}
