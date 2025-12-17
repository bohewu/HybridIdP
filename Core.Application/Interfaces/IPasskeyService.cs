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
}
