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

    /// <summary>
    /// Performs a refresh token rotation for the specified authorization/session.
    /// Implements one-time use refresh semantics and sliding expiration rules.
    /// Detects token reuse (replay) when the presented refresh token hash does not match the expected current value.
    /// </summary>
    /// <param name="userId">Application user ID (Guid).</param>
    /// <param name="authorizationId">OpenIddict authorization identifier.</param>
    /// <param name="presentedRefreshToken">Raw refresh token presented by the client (will be hashed internally).</param>
    /// <param name="ipAddress">IP address of the caller for audit metadata (optional).</param>
    /// <param name="userAgent">User agent string for audit/heuristics (optional).</param>
    Task<RefreshResultDto> RefreshAsync(Guid userId, string authorizationId, string presentedRefreshToken, string? ipAddress, string? userAgent);

    /// <summary>
    /// Revokes the entire session chain (authorization + all tracked refresh rotations) providing a reason.
    /// Returns structured details about the revocation outcome.
    /// </summary>
    /// <param name="userId">Application user ID.</param>
    /// <param name="authorizationId">Authorization identifier.</param>
    /// <param name="reason">Free-form administrator/user initiated reason string for audit.</param>
    Task<RevokeChainResultDto> RevokeChainAsync(Guid userId, string authorizationId, string reason);
}
