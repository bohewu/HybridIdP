namespace Core.Domain.Entities;

/// <summary>
/// Tracks ownership of custom OAuth scopes by Person.
/// Phase 13: Person-level role management and ApplicationManager role
/// Note: System scopes (openid, profile, email, roles) don't have ownership
/// </summary>
public class ScopeOwnership
{
    /// <summary>
    /// Primary key
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Scope name/identifier
    /// </summary>
    public string ScopeId { get; set; } = string.Empty;
    
    /// <summary>
    /// Person who created/owns this scope
    /// </summary>
    public Guid CreatedByPersonId { get; set; }
    
    /// <summary>
    /// Navigation property to Person
    /// </summary>
    public Person? CreatedByPerson { get; set; }
    
    /// <summary>
    /// When the scope was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// ApplicationUser who created the scope
    /// </summary>
    public Guid CreatedByUserId { get; set; }
    
    /// <summary>
    /// Navigation property to ApplicationUser
    /// </summary>
    public ApplicationUser? CreatedByUser { get; set; }
}
