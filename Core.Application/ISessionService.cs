using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Application.DTOs;

namespace Core.Application;

public interface ISessionService
{
    /// <summary>
    /// Lists active authorizations (sessions) for a given user.
    /// </summary>
    /// <param name="userId">Application user ID (Guid)</param>
    Task<IEnumerable<SessionDto>> ListSessionsAsync(Guid userId);

    /// <summary>
    /// Revokes a single authorization (session) if it belongs to the user.
    /// Returns true when a session was revoked, false otherwise.
    /// </summary>
    Task<bool> RevokeSessionAsync(Guid userId, string authorizationId);

    /// <summary>
    /// Revokes all authorizations (sessions) for the given user.
    /// Returns the number of revoked sessions.
    /// </summary>
    Task<int> RevokeAllSessionsAsync(Guid userId);
}
