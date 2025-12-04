namespace Core.Domain.Entities;

/// <summary>
/// Tracks ownership of OAuth clients by Person.
/// Phase 13: Person-level role management and ApplicationManager role
/// </summary>
public class ClientOwnership
{
    /// <summary>
    /// Primary key
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// OpenIddict Application/Client ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// Person who created/owns this client
    /// </summary>
    public Guid CreatedByPersonId { get; set; }
    
    /// <summary>
    /// Navigation property to Person
    /// </summary>
    public Person? CreatedByPerson { get; set; }
    
    /// <summary>
    /// When the client was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// ApplicationUser who created the client
    /// </summary>
    public Guid CreatedByUserId { get; set; }
    
    /// <summary>
    /// Navigation property to ApplicationUser
    /// </summary>
    public ApplicationUser? CreatedByUser { get; set; }
}
