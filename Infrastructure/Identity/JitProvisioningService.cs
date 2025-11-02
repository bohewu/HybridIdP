using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Infrastructure.Identity;

public class JitProvisioningService : IJitProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public JitProvisioningService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ApplicationUser> ProvisionUserAsync(LegacyUserDto dto, CancellationToken cancellationToken = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        if (!dto.IsAuthenticated)
            throw new InvalidOperationException("Legacy authentication failed; cannot provision user.");

        // Use email as username when available; otherwise, fall back to external id.
        var userName = dto.Email ?? dto.ExternalId ?? throw new InvalidOperationException("Cannot determine username for provisioning.");

        var existing = await _userManager.FindByNameAsync(userName);
        if (existing is null)
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = dto.Email,
                PhoneNumber = dto.Phone,
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to create user: " + string.Join(",", createResult.Errors.Select(e => e.Code)));
            }

            // Add basic claims derived from legacy profile (best-effort)
            var claimsToAdd = new List<Claim>();
            var nameValue = dto.FullName ?? dto.Email ?? userName;
            if (!string.IsNullOrWhiteSpace(nameValue))
            {
                claimsToAdd.Add(new Claim("name", nameValue));
            }
            if (!string.IsNullOrWhiteSpace(dto.Department))
            {
                claimsToAdd.Add(new Claim(AuthConstants.Claims.Department, dto.Department));
            }
            if (claimsToAdd.Count > 0)
            {
                await _userManager.AddClaimsAsync(user, claimsToAdd);
            }
            return user;
        }
        else
        {
            // Update basic fields
            existing.Email = dto.Email ?? existing.Email;
            existing.PhoneNumber = dto.Phone ?? existing.PhoneNumber;

            var updateResult = await _userManager.UpdateAsync(existing);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to update user: " + string.Join(",", updateResult.Errors.Select(e => e.Code)));
            }
            // Best-effort: add claims if present (this may duplicate; can be improved later)
            var claimsToAdd = new List<Claim>();
            var nameValue = dto.FullName ?? dto.Email ?? existing.UserName;
            if (!string.IsNullOrWhiteSpace(nameValue))
            {
                claimsToAdd.Add(new Claim("name", nameValue));
            }
            if (!string.IsNullOrWhiteSpace(dto.Department))
            {
                claimsToAdd.Add(new Claim(AuthConstants.Claims.Department, dto.Department));
            }
            if (claimsToAdd.Count > 0)
            {
                await _userManager.AddClaimsAsync(existing, claimsToAdd);
            }
            return existing;
        }
    }
}
