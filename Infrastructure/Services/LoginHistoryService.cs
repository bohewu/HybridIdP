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

        public LoginHistoryService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
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
            // Get recent login history (last 10 successful logins)
            var recentLogins = await GetLoginHistoryAsync(currentLogin.UserId, 10);

            if (!recentLogins.Any())
            {
                // First login, not abnormal
                return false;
            }

            // Check if IP address is new
            var knownIps = recentLogins.Select(l => l.IpAddress).Distinct().ToList();
            if (!string.IsNullOrEmpty(currentLogin.IpAddress) && !knownIps.Contains(currentLogin.IpAddress))
            {
                return true;
            }

            // Could add more checks here: user agent, time of day, etc.

            return false;
        }
    }
}