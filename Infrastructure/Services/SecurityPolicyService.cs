using System;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class SecurityPolicyService : ISecurityPolicyService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SecurityPolicyService> _logger;
        private const string SecurityPolicyCacheKey = "SecurityPolicy";

        public SecurityPolicyService(ApplicationDbContext context, IMemoryCache cache, ILogger<SecurityPolicyService> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        public async Task<SecurityPolicy> GetCurrentPolicyAsync()
        {
            if (_cache.TryGetValue(SecurityPolicyCacheKey, out SecurityPolicy? policy))
            {
                _logger.LogDebug("Retrieving security policy from cache.");
                return policy!;
            }

            policy = await _context.SecurityPolicies.FirstOrDefaultAsync();

            if (policy == null)
            {
                _logger.LogInformation("No security policy found in DB, creating default.");
                policy = new SecurityPolicy
                {
                    Id = Guid.NewGuid(),
                    MinPasswordLength = 6,
                    RequireUppercase = true,
                    RequireLowercase = true,
                    RequireDigit = true,
                    RequireNonAlphanumeric = true,
                    PasswordHistoryCount = 0,
                    PasswordExpirationDays = 0,
                    UpdatedUtc = DateTime.UtcNow,
                    UpdatedBy = "System"
                };
                _context.SecurityPolicies.Add(policy);
                await _context.SaveChangesAsync();
            }

            _cache.Set(SecurityPolicyCacheKey, policy, TimeSpan.FromMinutes(30)); // Cache for 30 minutes
            _logger.LogDebug("Security policy retrieved from DB and cached.");
            return policy;
        }

        public async Task UpdatePolicyAsync(SecurityPolicy policy, string? updatedBy = null)
        {
            var existingPolicy = await _context.SecurityPolicies.FirstOrDefaultAsync();

            if (existingPolicy == null)
            {
                _logger.LogWarning("Attempted to update non-existent security policy. Creating new one.");
                policy.Id = Guid.NewGuid();
                policy.UpdatedUtc = DateTime.UtcNow;
                policy.UpdatedBy = updatedBy ?? "System";
                _context.SecurityPolicies.Add(policy);
            }
            else
            {
                _logger.LogInformation("Updating existing security policy.");
                existingPolicy.MinPasswordLength = policy.MinPasswordLength;
                existingPolicy.RequireUppercase = policy.RequireUppercase;
                existingPolicy.RequireLowercase = policy.RequireLowercase;
                existingPolicy.RequireDigit = policy.RequireDigit;
                existingPolicy.RequireNonAlphanumeric = policy.RequireNonAlphanumeric;
                existingPolicy.PasswordHistoryCount = policy.PasswordHistoryCount;
                existingPolicy.PasswordExpirationDays = policy.PasswordExpirationDays;
                existingPolicy.UpdatedUtc = DateTime.UtcNow;
                existingPolicy.UpdatedBy = updatedBy ?? existingPolicy.UpdatedBy;
                _context.SecurityPolicies.Update(existingPolicy);
            }

            await _context.SaveChangesAsync();
            _cache.Remove(SecurityPolicyCacheKey); // Invalidate cache
            _logger.LogInformation("Security policy updated and cache invalidated.");
        }
    }
}
