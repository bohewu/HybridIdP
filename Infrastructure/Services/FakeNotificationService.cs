using Core.Application;
using Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public partial class FakeNotificationService : INotificationService
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
            LogAbnormalLogin(_logger, userId, login.IpAddress, login.LoginTime.ToString());
            return Task.CompletedTask;
        }
        [LoggerMessage(Level = LogLevel.Warning, Message = "Abnormal login detected for user {UserId} from IP {IpAddress} at {LoginTime}")]
        static partial void LogAbnormalLogin(ILogger logger, string? userId, string? ipAddress, string? loginTime);
    }
}