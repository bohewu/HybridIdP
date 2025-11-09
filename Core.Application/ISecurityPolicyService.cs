using System.Threading.Tasks;
using Core.Domain.Entities;

namespace Core.Application
{
    public interface ISecurityPolicyService
    {
        Task<SecurityPolicy> GetCurrentPolicyAsync();
        Task UpdatePolicyAsync(SecurityPolicy policy, string? updatedBy = null);
    }
}
