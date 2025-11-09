using Core.Application.DTOs;
using Core.Domain.Entities;
using System.Threading.Tasks;

namespace Core.Application;

public interface ISecurityPolicyService
{
    Task<SecurityPolicy> GetCurrentPolicyAsync();
    Task UpdatePolicyAsync(SecurityPolicyDto policyDto, string updatedBy);
}
