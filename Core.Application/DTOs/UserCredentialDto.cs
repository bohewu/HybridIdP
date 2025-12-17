namespace Core.Application.DTOs;

public class UserCredentialDto
{
    public int Id { get; set; }
    public string? DeviceName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
