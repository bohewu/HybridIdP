using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using OpenIddict.Abstractions;

namespace Infrastructure.Services;

public class SessionService : ISessionService
{
    private readonly IOpenIddictAuthorizationManager _authorizations;
    private readonly IOpenIddictApplicationManager _applications;
    private readonly IOpenIddictTokenManager _tokens;
    private readonly IApplicationDbContext _db;

    public SessionService(
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictApplicationManager applications,
        IOpenIddictTokenManager tokens,
        IApplicationDbContext dbContext)
    {
        _authorizations = authorizations;
        _applications = applications;
        _tokens = tokens;
        _db = dbContext;
    }

    public async Task<IEnumerable<SessionDto>> ListSessionsAsync(Guid userId)
    {
        var items = new List<SessionDto>();

        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(),
            client: null,
            status: null,
            type: null,
            scopes: ImmutableArray<string>.Empty))
        {
            var id = await _authorizations.GetIdAsync(authorization, CancellationToken.None) ?? string.Empty;

            // Best-effort: enrich session output with application information and status where available
            string? clientId = null;
            string? clientDisplayName = null;
            string? status = null;
            DateTime? createdAt = null; // stored as UTC
            DateTime? expiresAt = null; // stored as UTC

            try
            {
                var appId = await _authorizations.GetApplicationIdAsync(authorization, CancellationToken.None);
                if (!string.IsNullOrEmpty(appId))
                {
                    var app = await _applications.FindByIdAsync(appId, CancellationToken.None);
                    if (app is object)
                    {
                        clientId = await _applications.GetClientIdAsync(app, CancellationToken.None);
                        clientDisplayName = await _applications.GetDisplayNameAsync(app, CancellationToken.None);
                    }
                }

                status = await _authorizations.GetStatusAsync(authorization, CancellationToken.None);
                var creationOffset = await _authorizations.GetCreationDateAsync(authorization, CancellationToken.None);
                if (creationOffset.HasValue)
                    createdAt = creationOffset.Value.UtcDateTime;

                // Attempt to discover an expiration based on associated tokens (choose latest valid token expiration)
                if (clientId is not null)
                {
                    try
                    {
                        var candidateExpirations = new List<DateTimeOffset>();
                        await foreach (var token in _tokens.FindAsync(
                            subject: userId.ToString(),
                            client: clientId,
                            status: null,
                            type: null))
                        {
                            try
                            {
                                var tokenAuthId = await _tokens.GetAuthorizationIdAsync(token, CancellationToken.None);
                                if (!string.IsNullOrEmpty(tokenAuthId) && tokenAuthId == id)
                                {
                                    var tokenStatus = await _tokens.GetStatusAsync(token, CancellationToken.None);
                                    if (string.Equals(tokenStatus, OpenIddictConstants.Statuses.Valid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var expOffset = await _tokens.GetExpirationDateAsync(token, CancellationToken.None);
                                        if (expOffset.HasValue)
                                            candidateExpirations.Add(expOffset.Value);
                                    }
                                }
                            }
                            catch { /* ignore token enrichment errors */ }
                        }
                        if (candidateExpirations.Count > 0)
                        {
                            expiresAt = candidateExpirations.Max().UtcDateTime;
                        }
                    }
                    catch { /* ignore token enumeration failures */ }
                }
            }
            catch
            {
                // Swallow enrichment failures - listing sessions should be best-effort
            }

            items.Add(new SessionDto(
                AuthorizationId: id,
                ClientId: clientId,
                ClientDisplayName: clientDisplayName,
                CreatedAt: createdAt,
                ExpiresAt: expiresAt,
                Status: status));
        }

        return items;
    }

    public async Task<bool> RevokeSessionAsync(Guid userId, string authorizationId)
    {
        var authorization = await _authorizations.FindByIdAsync(authorizationId, CancellationToken.None);
        if (authorization is null)
            return false;

        var subject = await _authorizations.GetSubjectAsync(authorization, CancellationToken.None);
        if (!string.Equals(subject, userId.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;

        var ok = await _authorizations.TryRevokeAsync(authorization, CancellationToken.None);
        if (ok)
        {
            try
            {
                // Best-effort: revoke tokens associated with the authorization (OpenIddict supports this)
                // If token manager exposes RevokeByAuthorizationIdAsync, use it to remove tokens.
                await _tokens.RevokeByAuthorizationIdAsync(authorizationId, CancellationToken.None);
            }
            catch
            {
                // ignore token-revocation failures; authorization revocation is primary
            }
        }
        return ok;
    }

    public async Task<int> RevokeAllSessionsAsync(Guid userId)
    {
        var count = 0;

        // Materialize the list first to avoid potential iterator side-effects while revoking.
        var toRevoke = new List<object>();
        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(),
            client: null,
            status: OpenIddictConstants.Statuses.Valid,
            type: null,
            scopes: System.Collections.Immutable.ImmutableArray<string>.Empty))
        {
            toRevoke.Add(authorization);
        }

        // Fallback: if none were returned with status=valid but sessions exist with other statuses, allow revoking them too.
        if (toRevoke.Count == 0)
        {
            await foreach (var authorization in _authorizations.FindAsync(
                subject: userId.ToString(),
                client: null,
                status: null, // broader search
                type: null,
                scopes: System.Collections.Immutable.ImmutableArray<string>.Empty))
            {
                toRevoke.Add(authorization);
            }
        }

        foreach (var authorization in toRevoke)
        {
            try
            {
                if (await _authorizations.TryRevokeAsync(authorization, CancellationToken.None))
                {
                    try
                    {
                        var id = await _authorizations.GetIdAsync(authorization, CancellationToken.None) ?? string.Empty;
                        if (!string.IsNullOrEmpty(id))
                        {
                            await _tokens.RevokeByAuthorizationIdAsync(id, CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // token revocation is best-effort
                    }
                    count++;
                }
            }
            catch
            {
                // ignore individual revocation errors to continue processing remaining authorizations
            }
        }

        return count;
    }

    public Task<RefreshResultDto> RefreshAsync(Guid userId, string authorizationId, string presentedRefreshToken, string? ipAddress, string? userAgent)
    {
        // Placeholder implementation for TDD; will be replaced by real logic.
        throw new NotImplementedException("RefreshAsync lifecycle rotation not yet implemented.");
    }

    public Task<RevokeChainResultDto> RevokeChainAsync(Guid userId, string authorizationId, string reason)
    {
        // Placeholder implementation for TDD; will be replaced by real logic.
        throw new NotImplementedException("RevokeChainAsync lifecycle revocation not yet implemented.");
    }
}
