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

            // Best-effort: not all providers expose creation/expiry/status via abstractions consistently.
            // v1 surfaces only stable fields; can be enriched later if needed.
            items.Add(new SessionDto(
                AuthorizationId: id,
                ClientId: null,
                ClientDisplayName: null,
                CreatedAt: null,
                ExpiresAt: null,
                Status: null));
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
        return ok;
    }

    public async Task<int> RevokeAllSessionsAsync(Guid userId)
    {
        var count = 0;
        await foreach (var authorization in _authorizations.FindAsync(
            subject: userId.ToString(), client: null, status: null, type: null, scopes: System.Collections.Immutable.ImmutableArray<string>.Empty))
        {
            if (await _authorizations.TryRevokeAsync(authorization, CancellationToken.None))
                count++;
        }
        return count;
    }
}
