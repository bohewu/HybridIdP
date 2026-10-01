using System.Security.Claims;
using Core.Application.Ports;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class HttpContextRecoveryProofAuthorizer : IRecoveryProofAuthorizer
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Microsoft.AspNetCore.Authorization.IAuthorizationService _authorizationService;
    private readonly RecoveryEmailSelectionOptions _selection;

    public HttpContextRecoveryProofAuthorizer(
        IHttpContextAccessor httpContextAccessor,
        Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService,
        IOptions<RecoveryEmailSelectionOptions>? selection = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
        _selection = selection?.Value ?? new RecoveryEmailSelectionOptions();
    }

    public Task<bool> IsSelfServiceAuthorizedAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = _httpContextAccessor.HttpContext?.User;
        var session = _httpContextAccessor.HttpContext?.Features.Get<ISessionFeature>()?.Session;
        return Task.FromResult(
            (!_selection.Enabled || principal?.Identity?.AuthenticationType == IdentityConstants.ApplicationScheme) &&
            PrincipalAccountId(principal) == localAccountId && HasCompletedMfa(principal) &&
            session?.GetString("required-password-change.pending") is null &&
            session?.GetString("credential-migration.continuation") is null &&
            session?.GetString("credential-migration.recovery-continuation") is null);
    }

    public async Task<bool> IsAdministratorAuthorizedAsync(
        Guid actorAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = _httpContextAccessor.HttpContext?.User;
        if (PrincipalAccountId(principal) != actorAccountId || !HasCompletedMfa(principal))
        {
            return false;
        }

        return (await _authorizationService.AuthorizeAsync(principal!, null, Permissions.Users.Update)).Succeeded;
    }

    private static Guid? PrincipalAccountId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static bool HasCompletedMfa(ClaimsPrincipal? principal) =>
        HasAuthenticationMethod(principal, AuthConstants.Amr.Mfa) ||
        HasAuthenticationMethod(principal, AuthConstants.Amr.HardwareKey);

    private static bool HasAuthenticationMethod(ClaimsPrincipal? principal, string method) =>
        principal?.Claims.Any(claim =>
            (claim.Type == AuthConstants.ClaimTypes.Amr || claim.Type == AuthConstants.ClaimTypes.AuthenticationMethod) &&
            string.Equals(claim.Value, method, StringComparison.OrdinalIgnoreCase)) == true;
}
