using System;
using System.Collections.Generic;

namespace Core.Application.DTOs;

public sealed record SessionDto(
    string AuthorizationId,
    string? ClientId,
    string? ClientDisplayName,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? Status
);
