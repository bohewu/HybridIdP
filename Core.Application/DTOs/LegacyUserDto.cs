namespace Core.Application.DTOs;

public class LegacyUserDto
{
    public bool IsAuthenticated { get; set; }
    public string? ExternalId { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? EmployeeId { get; set; }
    
    // Identity Fields
    public string? NationalId { get; set; }
    public string? PassportNumber { get; set; }
    public string? ResidentCertificateNumber { get; set; }
}
