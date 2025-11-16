using Core.Application;
using Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class FakeNotificationService : INotificationService
    {
        private readonly ILogger<FakeNotificationService> _logger;

        public FakeNotificationService(ILogger<FakeNotificationService> logger)
        {
            _logger = logger;
        }

        public Task NotifyAbnormalLoginAsync(string userId, LoginHistory login)
        {
            // Fake implementation: just log the event
            // Future: implement SMS/email notifications
            _logger.LogWarning("Abnormal login detected for user {UserId} from IP {IpAddress} at {LoginTime}", userId, login.IpAddress, login.LoginTime);
            return Task.CompletedTask;
        }
    }
}