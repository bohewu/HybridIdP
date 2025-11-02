namespace Core.Application.DTOs;

public class LegacyUserDto
{
    public bool IsAuthenticated { get; set; }
    public string? ExternalId { get; set; }
    public string? IdCardNumber { get; set; }
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
