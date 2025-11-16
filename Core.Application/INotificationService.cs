using Core.Domain.Entities;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface INotificationService
    {
        /// <summary>
        /// Notifies about an abnormal login event.
        /// Currently implemented as a fake service; future: implement SMS/email notifications.
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="login">The login history entry</param>
        Task NotifyAbnormalLoginAsync(string userId, LoginHistory login);
    }
}