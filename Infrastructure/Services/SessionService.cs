using System;
using System.Collections.Generic;
using System.Linq;
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

    public SessionService(
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictApplicationManager applications,
        IOpenIddictTokenManager tokens)
    {
        _authorizations = authorizations;
        _applications = applications;
        _tokens = tokens;
    }

    public async Task<IEnumerable<SessionDto>> ListSessionsAsync(Guid userId)
    {
        var items = new List<SessionDto>();

        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(),
            client: null,
            status: null,
            type: null,
            scopes: System.Collections.Immutable.ImmutableArray<string>.Empty))
        {
            var id = await _authorizations.GetIdAsync(authorization, CancellationToken.None) ?? string.Empty;

            // Best-effort: enrich session output with application information and status where available
            string? clientId = null;
            string? clientDisplayName = null;
            string? status = null;

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
            }
            catch
            {
                // Swallow enrichment failures - listing sessions should be best-effort
            }

            items.Add(new SessionDto(
                AuthorizationId: id,
                ClientId: clientId,
                ClientDisplayName: clientDisplayName,
                CreatedAt: null,
                ExpiresAt: null,
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
        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(), client: null, status: null, type: null, scopes: System.Collections.Immutable.ImmutableArray<string>.Empty))
        {
            if (await _authorizations.TryRevokeAsync(authorization, CancellationToken.None))
            {
                try
                {
                    var id = await _authorizations.GetIdAsync(authorization, CancellationToken.None) ?? string.Empty;
                    await _tokens.RevokeByAuthorizationIdAsync(id, CancellationToken.None);
                }
                catch
                {
                    // best-effort only
                }
                count++;
            }
        }
        return count;
    }
}
