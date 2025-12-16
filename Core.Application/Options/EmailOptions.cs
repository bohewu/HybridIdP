using System.ComponentModel.DataAnnotations;

namespace Core.Application.Options;

public class EmailOptions
{
    public const string SectionName = "EmailSettings";

    [Required]
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    
    [Required]
    public string FromAddress { get; set; } = string.Empty;
    
    [Required]
    public string FromName { get; set; } = string.Empty;
}
