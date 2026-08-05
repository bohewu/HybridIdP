using System.ComponentModel.DataAnnotations;

namespace Core.Application;

public class MailSettingsDto
{
    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    [MaxLength(320)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Password { get; set; } = string.Empty;

    public bool EnableSsl { get; set; } = true;

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string FromAddress { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string FromName { get; set; } = string.Empty;
}
