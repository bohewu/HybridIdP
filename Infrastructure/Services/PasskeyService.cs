using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.WebUtilities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Core.Application.Interfaces;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure; // For ApplicationDbContext

namespace Infrastructure.Services;

public class PasskeyService : IPasskeyService
{
    private readonly IFido2 _fido2;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PasskeyService> _logger;

    public PasskeyService(
        IFido2 fido2,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        ILogger<PasskeyService> logger)
    {
        _fido2 = fido2;
        _userManager = userManager;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CredentialCreateOptions> GetRegistrationOptionsAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var userEntity = new Fido2User
        {
            DisplayName = user.UserName,
            Name = user.Email ?? user.UserName,
            Id = Encoding.UTF8.GetBytes(user.Id.ToString()) // Use user ID for proper identification
        };

        // Get existing credentials to exclude
        var existingCredentials = await _dbContext.UserCredentials
            .Where(c => c.UserId == user.Id)
            .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))
            .ToListAsync(ct);

        var authenticatorSelection = new AuthenticatorSelection
        {
            ResidentKey = ResidentKeyRequirement.Required,
            UserVerification = UserVerificationRequirement.Preferred
        };

        // Fido2 v4 uses a params object with object initializer
        var requestParams = new RequestNewCredentialParams
        {
            User = userEntity, 
            ExcludeCredentials = existingCredentials,
            AuthenticatorSelection = authenticatorSelection, 
            AttestationPreference = AttestationConveyancePreference.None,
            Extensions = null
        };

        var options = _fido2.RequestNewCredential(requestParams);

        return options;
    }

    public async Task<(bool Success, string? Error)> RegisterCredentialsAsync(
        ApplicationUser user, 
        string jsonResponse, 
        string originalOptionsJson, 
        CancellationToken ct = default)
    {
        try
        {
            // 1. Parse the attestation response
            var attestationResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(jsonResponse);
            if (attestationResponse == null)
            {
                return (false, "Invalid attestation response");
            }
            
            var options = CredentialCreateOptions.FromJson(originalOptionsJson);
            
            // 2. Verify with Fido2 using v4 API
            var makeCredentialParams = new MakeNewCredentialParams
            {
                AttestationResponse = attestationResponse,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (args, cancellationToken) =>
                {
                    // Callback: Check if credential ID is unique
                    var credIdBytes = args.CredentialId;
                    var exists = await _dbContext.UserCredentials
                        .AnyAsync(c => c.CredentialId == credIdBytes, cancellationToken);
                    return !exists; // Return true if unique  
                }
            };
            
            var result = await _fido2.MakeNewCredentialAsync(makeCredentialParams, ct);
            
            // v4 API throws on failure, so if we get here it's successful
            // The result is directly a RegisteredPublicKeyCredential
            
            // 3. Extract device name from response (if provided)
            string? deviceName = null;
            try
            {
                var json = JsonDocument.Parse(jsonResponse);
                if (json.RootElement.TryGetProperty("deviceName", out var deviceNameProp))
                {
                    deviceName = deviceNameProp.GetString();
                }
            }
            catch { /* Ignore parsing errors */ }
            
            // 4. Save credential to database
            var credential = new UserCredential
            {
                UserId = user.Id,
                CredentialId = result.Id,
                PublicKey = result.PublicKey,
                SignatureCounter = result.SignCount,
                CredType = result.Type.ToString(),
                RegDate = DateTime.UtcNow,
                AaGuid = result.AaGuid,
                DeviceName = deviceName ?? "Unknown Device"
            };
            
            _dbContext.UserCredentials.Add(credential);
            await _dbContext.SaveChangesAsync(ct);
            
            _logger.LogInformation("Passkey registered successfully for user {UserId}", user.Id);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register passkey for user {UserId}", user.Id);
            return (false, "Registration failed");
        }
    }

    public async Task<AssertionOptions> GetAssertionOptionsAsync(string? username, CancellationToken ct = default)
    {
        var allowedCredentials = new List<PublicKeyCredentialDescriptor>();
        
        // If username is provided, get credentials for that user
        if (!string.IsNullOrEmpty(username))
        {
            var user = await _userManager.FindByNameAsync(username);
            if (user != null)
            {
                allowedCredentials = await _dbContext.UserCredentials
                    .Where(c => c.UserId == user.Id)
                    .Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))
                    .ToListAsync(ct);
            }
        }
        
        // Fido2 v4 GetAssertionOptions 
        var options = _fido2.GetAssertionOptions(
            new GetAssertionOptionsParams
            {
                AllowedCredentials = allowedCredentials,
                UserVerification = UserVerificationRequirement.Preferred,
                Extensions = null
            });
            
        return options;
    }

    public async Task<(bool Success, ApplicationUser? User, string? Error)> VerifyAssertionAsync(
        string jsonResponse, 
        string originalOptionsJson, 
        CancellationToken ct = default)
    {
        try
        {
            // 1. Parse the assertion response
            var assertionResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(jsonResponse);
            if (assertionResponse == null)
            {
                return (false, null, "Invalid assertion response");
            }
            
            var options = AssertionOptions.FromJson(originalOptionsJson);
            
            // 2. Find the credential by ID
            // Note: EF Core can't translate SequenceEqual to SQL, so we use RawId bytes directly
            var credentialIdBytes = Base64UrlTextEncoder.Decode(assertionResponse.Id); // Id is Base64Url string
            
            // Use Contains check which SQL Server/PostgreSQL can handle for byte arrays
            var credential = await _dbContext.UserCredentials
                .Include(c => c.User)
                    .ThenInclude(u => u.Person) // Important for Person.Status check
                .FirstOrDefaultAsync(c => c.CredentialId == credentialIdBytes, ct);
            
            if (credential == null)
            {
                _logger.LogWarning("Passkey credential not found: {CredentialId}", assertionResponse.Id);
                return (false, null, "Invalid credential");
            }
            
            // 3. Verify the assertion using v4 API
            var makeAssertionParams = new MakeAssertionParams
            {
                AssertionResponse = assertionResponse,
                OriginalOptions = options,
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = credential.SignatureCounter,
                IsUserHandleOwnerOfCredentialIdCallback = (args, cancellationToken) =>
                {
                    // Callback: user handle verification (optional)
                    return Task.FromResult(true);
                }
            };
            
            var result = await _fido2.MakeAssertionAsync(makeAssertionParams, ct);
            
            // v4 API throws on failure, so if we get here it's successful
            
            // 4. Update signature counter (防止 replay attacks)
            credential.SignatureCounter = result.SignCount;
            credential.LastUsedAt = DateTime.UtcNow; // Track usage
            await _dbContext.SaveChangesAsync(ct);
            
            _logger.LogInformation("Passkey verification successful for user {UserId}", credential.UserId);
            return (true, credential.User, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify passkey assertion");
            return (false, null, "Verification failed");
        }
    }

    public async Task<List<UserCredentialDto>> GetUserPasskeysAsync(Guid userId, CancellationToken ct = default)
    {
        var credentials = await _dbContext.UserCredentials
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.RegDate)
            .Select(c => new UserCredentialDto
            {
                Id = c.Id,
                DeviceName = c.DeviceName,
                CreatedAt = c.RegDate,
                LastUsedAt = c.LastUsedAt
            })
            .ToListAsync(ct);

        // Ensure DateTimeKind is UTC for proper JSON serialization (Z suffix)
        foreach (var cred in credentials)
        {
            cred.CreatedAt = DateTime.SpecifyKind(cred.CreatedAt, DateTimeKind.Utc);
            if (cred.LastUsedAt.HasValue)
            {
                cred.LastUsedAt = DateTime.SpecifyKind(cred.LastUsedAt.Value, DateTimeKind.Utc);
            }
        }

        return credentials;
    }

    public async Task<bool> DeletePasskeyAsync(Guid userId, int credentialId, CancellationToken ct = default)
    {
        var credential = await _dbContext.UserCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Id == credentialId, ct);
        
        if (credential == null)
        {
            return false; // Not found or not owned by user
        }
        
        _dbContext.UserCredentials.Remove(credential);
        await _dbContext.SaveChangesAsync(ct);
        
        _logger.LogInformation("Deleted passkey {CredentialId} for user {UserId}", credentialId, userId);
        return true;
    }
}
