using System.Threading;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Core.Domain;

namespace Core.Application.Interfaces;

public interface IPasskeyService
{
    Task<CredentialCreateOptions> GetRegistrationOptionsAsync(ApplicationUser user, CancellationToken ct = default);
    
    Task<Core.Application.DTOs.PasskeyRegistrationResult> RegisterCredentialsAsync(
        ApplicationUser user, 
        string jsonResponse, 
        string originalOptionsJson, 
        CancellationToken ct = default);
        
    Task<AssertionOptions> GetAssertionOptionsAsync(Guid? authenticatedUserId, CancellationToken ct = default);
    
    Task<(bool Success, ApplicationUser? User, bool UserVerified, string? Error)> VerifyAssertionAsync(
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
    Task<MfaRemovalResult> DeletePasskeyAsync(Guid userId, int credentialId, CancellationToken ct = default);
}

