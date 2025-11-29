namespace Core.Application.DTOs;

/// <summary>
/// DTO for creating or updating a Person
/// Phase 10.2: Person Service & API
/// </summary>
public class PersonDto
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Nickname { get; set; }
    public string? EmployeeId { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? ProfileUrl { get; set; }
    public string? PictureUrl { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }  // JSON string
    public string? Birthdate { get; set; }  // ISO 8601 format (YYYY-MM-DD)
    public string? Gender { get; set; }
    public string? TimeZone { get; set; }
    public string? Locale { get; set; }
}

/// <summary>
/// DTO for Person response with linked accounts
/// </summary>
public class PersonResponseDto : PersonDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public List<LinkedAccountDto>? Accounts { get; set; }
}

/// <summary>
/// DTO for linked account information
/// </summary>
public class LinkedAccountDto
{
    public Guid Id { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginDate { get; set; }
}

/// <summary>
/// DTO for linking an account to a person
/// </summary>
public class LinkAccountDto
{
    public Guid UserId { get; set; }
}

/// <summary>
/// DTO for paginated Person list response
/// </summary>
public class PersonListResponseDto
{
    public List<PersonResponseDto> Persons { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
