using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Web.IdP.Services;

// This supplements, rather than replaces, each endpoint's granular permission.
public sealed record DataSourceAdministrationRequirement(bool AllowAdministrativeBearer) : IAuthorizationRequirement
{
    public const string InteractivePolicy = "DataSourcesInteractiveAdmin";
    public const string AdministrationPolicy = "DataSourcesAdministration";
}

public sealed class DataSourceAdministrationAuthorizationHandler(IAdministrativeAuthorizationBoundary boundary)
    : AuthorizationHandler<DataSourceAdministrationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        DataSourceAdministrationRequirement requirement)
    {
        var authority = await boundary.ResolveAsync();
        if (authority?.Principal.Identity?.IsAuthenticated != true) return;
        if (authority.IsBearer
                ? requirement.AllowAdministrativeBearer
                : AuthorizationRoleClaimResolver.IsInIdpRole(authority.Principal, AuthConstants.Roles.Admin))
            context.Succeed(requirement);
    }
}
