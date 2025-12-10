using Core.Application;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class LoginHistoryService : ILoginHistoryService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ISecurityPolicyService _securityPolicyService;

        public LoginHistoryService(ApplicationDbContext dbContext, ISecurityPolicyService securityPolicyService)
        {
            _dbContext = dbContext;
            _securityPolicyService = securityPolicyService;
        }

        public async Task RecordLoginAsync(LoginHistory login)
        {
            _dbContext.LoginHistories.Add(login);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<LoginHistory>> GetLoginHistoryAsync(Guid userId, int count = 10)
        {
            return await _dbContext.LoginHistories
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.LoginTime)
                .Take(count)
                .ToListAsync();
        }

        public async Task<bool> DetectAbnormalLoginAsync(LoginHistory currentLogin)
        {
            var policy = await _securityPolicyService.GetCurrentPolicyAsync();
            var historyCount = policy.AbnormalLoginHistoryCount;

            // Get recent login history
            var recentLogins = await GetLoginHistoryAsync(currentLogin.UserId, historyCount);

            if (recentLogins.Count() == 0)
            {
                // First login, not abnormal
                return false;
            }

            // Check if IP address is new and not approved
            var knownIps = recentLogins
                .Where(l => !string.IsNullOrEmpty(l.IpAddress))
                .Select(l => l.IpAddress!)
                .Distinct()
                .ToList();
            
            // Also include IPs from approved abnormal logins
            var approvedAbnormalIps = await _dbContext.LoginHistories
                .Where(l => l.UserId == currentLogin.UserId && l.IsFlaggedAbnormal && l.IsApprovedByAdmin && !string.IsNullOrEmpty(l.IpAddress))
                .Select(l => l.IpAddress!)
                .Distinct()
                .ToListAsync();
            
            knownIps.AddRange(approvedAbnormalIps);
            knownIps = knownIps.Distinct().ToList();

            if (!string.IsNullOrEmpty(currentLogin.IpAddress) && !knownIps.Contains(currentLogin.IpAddress))
            {
                return true;
            }

            // Could add more checks here: user agent, time of day, etc.

            return false;
        }

        public async Task<bool> ApproveAbnormalLoginAsync(int loginHistoryId)
        {
            var loginHistory = await _dbContext.LoginHistories.FindAsync(loginHistoryId);
            if (loginHistory == null || !loginHistory.IsFlaggedAbnormal)
            {
                return false;
            }

            loginHistory.IsApprovedByAdmin = true;
            await _dbContext.SaveChangesAsync();
            return true;
        }
    }
}