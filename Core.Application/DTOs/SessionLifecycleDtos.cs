using System;

namespace Core.Application.DTOs;

/// <summary>
/// Result of a refresh token rotation operation.
/// </summary>
public sealed record RefreshResultDto(
    string AuthorizationId,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    bool SlidingExtended,
    bool ReuseDetected
);

/// <summary>
/// Result of revoking an entire session chain.
/// </summary>
public sealed record RevokeChainResultDto(
    string AuthorizationId,
    int TokensRevoked,
    bool AlreadyRevoked
);