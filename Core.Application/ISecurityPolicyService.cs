using Core.Application.DTOs;
using Core.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Application;

public interface ISecurityPolicyService
{
    Task<SecurityPolicy> GetCurrentPolicyAsync();
    Task<SecurityPolicy> GetCurrentPolicyForPasskeyAuthenticationAsync(CancellationToken ct = default);
    Task UpdatePolicyAsync(SecurityPolicyDto policyDto, string updatedBy);
}
