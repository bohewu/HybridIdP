using System.Security.Claims;
using Core.Application.Ports;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Services;

public sealed class HttpContextRecoveryProofAuthorizer : IRecoveryProofAuthorizer
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Microsoft.AspNetCore.Authorization.IAuthorizationService _authorizationService;

    public HttpContextRecoveryProofAuthorizer(
        IHttpContextAccessor httpContextAccessor,
        Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public Task<bool> IsSelfServiceAuthorizedAsync(
        Guid localAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = _httpContextAccessor.HttpContext?.User;
        return Task.FromResult(
            PrincipalAccountId(principal) == localAccountId && HasCompletedMfa(principal));
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
