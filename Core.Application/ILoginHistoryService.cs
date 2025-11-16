using Core.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Application
{
    public interface ILoginHistoryService
    {
        /// <summary>
        /// Records a login event in the history.
        /// </summary>
        /// <param name="login">The login history entry to record</param>
        Task RecordLoginAsync(LoginHistory login);

        /// <summary>
        /// Gets the recent login history for a user.
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="count">Number of recent logins to retrieve</param>
        /// <returns>List of recent login histories</returns>
        Task<IEnumerable<LoginHistory>> GetLoginHistoryAsync(Guid userId, int count = 10);

        /// <summary>
        /// Approves an abnormal login, allowing the IP address for future logins.
        /// </summary>
        /// <param name="loginHistoryId">The ID of the login history entry to approve</param>
        /// <returns>True if approved, false if not found or not abnormal</returns>
        Task<bool> ApproveAbnormalLoginAsync(int loginHistoryId);

        /// <summary>
        /// Detects if the current login is abnormal based on history.
        /// </summary>
        /// <param name="currentLogin">The current login attempt</param>
        /// <returns>True if abnormal, false otherwise</returns>
        Task<bool> DetectAbnormalLoginAsync(LoginHistory currentLogin);
    }
}