using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Web.IdP.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class RequireClientPermissionAttribute : TypeFilterAttribute
    {
        public RequireClientPermissionAttribute(string permission)
            : base(typeof(RequireClientPermissionFilter))
        {
            Arguments = new object[] { permission };
        }
    }

    public class RequireClientPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _permission;
        private readonly IOpenIddictApplicationManager _applicationManager;

        public RequireClientPermissionFilter(string permission, IOpenIddictApplicationManager applicationManager)
        {
            _permission = permission;
            _applicationManager = applicationManager;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Retrieve the OpenIddict request
            // Retrieve the OpenIddict server feature
            var feature = context.HttpContext.Features.Get<OpenIddictServerAspNetCoreFeature>();
            var request = feature?.Transaction?.Request;
            
            if (request == null)
            {
                // If we can't get the request, we can't validate the client.
                // In a proper OpenIddict configuration, the request should be available.
                context.Result = new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.ServerError,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The OpenID Connect request cannot be retrieved."
                    }));
                return;
            }

            // Retrieve the client ID from the request
            // Note: In Passthrough mode, OpenIddict might have already extracted the client_id
            var clientId = request.ClientId;
            if (string.IsNullOrEmpty(clientId))
            {
                 // If no client_id is present, we cannot validate client permissions.
                 // For endpoints requiring client authentication (like Token), this is a fail.
                 // For Auth endpoint, if client_id is missing, it's also invalid OIDC request.
                 context.Result = new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client application logic cannot be determined."
                    }));
                 return;
            }

            // Retrieve the client application
            var client = await _applicationManager.FindByClientIdAsync(clientId);
            if (client == null)
            {
                context.Result = new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client application is unknown."
                    }));
                return;
            }

            // Check if the client has the required permission
            var permissions = await _applicationManager.GetPermissionsAsync(client);
            if (!permissions.Contains(_permission, StringComparer.OrdinalIgnoreCase))
            {
                context.Result = new ForbidResult(
                    authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                    properties: new AuthenticationProperties(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.UnauthorizedClient,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The client is not authorized to use this endpoint."
                    }));
                return;
            }

            // Validation passed
        }
    }
}
