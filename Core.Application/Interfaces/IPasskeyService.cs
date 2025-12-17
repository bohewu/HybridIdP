using System.Threading;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Core.Domain;

namespace Core.Application.Interfaces;

public interface IPasskeyService
{
    Task<CredentialCreateOptions> GetRegistrationOptionsAsync(ApplicationUser user, CancellationToken ct = default);
    
    Task<(bool Success, string? Error)> RegisterCredentialsAsync(
        ApplicationUser user, 
        string jsonResponse, 
        string originalOptionsJson, 
        CancellationToken ct = default);
        
    Task<AssertionOptions> GetAssertionOptionsAsync(string? username, CancellationToken ct = default);
    
    Task<(bool Success, ApplicationUser? User, string? Error)> VerifyAssertionAsync(
        string jsonResponse, 
        string originalOptionsJson, 
        CancellationToken ct = default);
        
    /// <summary>
    /// Get all passkeys registered by a user
    /// </summary>
    Task<List<DTOs.UserCredentialDto>> GetUserPasskeysAsync(Guid userId, CancellationToken ct = default);
    
    /// <summary>
    /// Delete a passkey by ID (must be owned by the specified user)
    /// </summary>
    Task<bool> DeletePasskeyAsync(Guid userId, int credentialId, CancellationToken ct = default);
}

