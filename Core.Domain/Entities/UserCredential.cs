using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Entities;

public class UserCredential
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [Required]
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    [Required]
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();

    public uint SignatureCounter { get; set; }

    public string? CredType { get; set; }

    public DateTime RegDate { get; set; }

    public Guid AaGuid { get; set; }
    
    // Optional: Store device name/model for UI
    public string? DeviceName { get; set; }
}
