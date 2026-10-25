using Core.Application;
using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Web.IdP.Services;

public static class ClientProtocolPolicyHandlers
{
    // These handlers run after native client, redirect, permission and PKCE validation.
    // For request_uri authorization, OpenIddict has already restored the authenticated PAR parameters.
    public sealed class ValidateAuthorization(IOpenIddictApplicationManager applications)
        : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
            OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
                .UseScopedHandler<ValidateAuthorization>()
                .SetOrder(OpenIddictServerHandlers.Authentication.ValidateAuthorizedParty.Descriptor.Order + 500)
                .Build();

        public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context) =>
            await ValidateAuthorizationAsync(applications, context, context.Request);
    }

    public sealed class ValidatePushedAuthorization(IOpenIddictApplicationManager applications)
        : IOpenIddictServerHandler<ValidatePushedAuthorizationRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
            OpenIddictServerHandlerDescriptor.CreateBuilder<ValidatePushedAuthorizationRequestContext>()
                .UseScopedHandler<ValidatePushedAuthorization>()
                .SetOrder(OpenIddictServerHandlers.Authentication.ValidatePushedAuthorizedParty.Descriptor.Order + 500)
                .Build();

        public async ValueTask HandleAsync(ValidatePushedAuthorizationRequestContext context) =>
            await ValidateAuthorizationAsync(applications, context, context.Request);
    }

    private static async ValueTask ValidateAuthorizationAsync(
        IOpenIddictApplicationManager applications, BaseValidatingContext context, OpenIddictRequest request)
    {
        var application = await applications.FindByClientIdAsync(request.ClientId!, context.CancellationToken);
        if (application is null)
        {
            context.Reject(Errors.InvalidClient, "The client registration is unavailable.");
            return;
        }
        var policy = ClientProtocolPolicy.Read(await applications.GetPropertiesAsync(application, context.CancellationToken));
        if (!policy.IsValid)
        {
            context.Reject(Errors.InvalidRequest, "The client protocol policy is invalid.");
            return;
        }
        // An algorithm restriction is not a requirement to supply a challenge.
        if (policy.PkceMethodPolicy == CodeChallengeMethods.Sha256 &&
            !string.IsNullOrEmpty(request.CodeChallenge) && request.CodeChallengeMethod != CodeChallengeMethods.Sha256)
        {
            context.Reject(Errors.InvalidRequest, "This client requires the S256 code challenge method when PKCE is used.");
            return;
        }
        if (policy.ResponseModePolicy != ClientProtocolPolicy.Inherit)
        {
            // Do not change the wire response mode: a code request without response_mode uses query.
            var mode = request.ResponseMode;
            if (string.IsNullOrEmpty(mode) && request.IsAuthorizationCodeFlow()) mode = ResponseModes.Query;
            if (!string.Equals(mode, policy.ResponseModePolicy, StringComparison.Ordinal))
                context.Reject(Errors.InvalidRequest, "The response_mode is not permitted for this client.");
        }
    }

    public sealed class ValidateTokenAuthentication(IOpenIddictApplicationManager applications)
        : IOpenIddictServerHandler<ValidateTokenRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
            OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenRequestContext>()
                .UseScopedHandler<ValidateTokenAuthentication>()
                .SetOrder(OpenIddictServerHandlers.Exchange.ValidateAuthentication.Descriptor.Order + 500)
                .Build();

        public async ValueTask HandleAsync(ValidateTokenRequestContext context)
        {
            if (string.IsNullOrEmpty(context.ClientId)) return;
            var application = await applications.FindByClientIdAsync(context.ClientId, context.CancellationToken);
            if (application is null)
            {
                context.Reject(Errors.InvalidClient, "The client registration is unavailable.");
                return;
            }
            var policy = ClientProtocolPolicy.Read(await applications.GetPropertiesAsync(application, context.CancellationToken));
            if (!policy.IsValid)
            {
                context.Reject(Errors.InvalidClient, "The client protocol policy is invalid.");
                return;
            }
            if (policy.TokenEndpointAuthMethod == ClientProtocolPolicy.Inherit) return;

            // OpenIddict 7.7.1 normalizes Basic credentials into Request.ClientSecret.
            // After native extraction/authentication rejected malformed, mixed or invalid credentials,
            // retain their HTTP origin using the same Basic scheme comparison as the native extractor.
            // No OAuth request parameter can assert the authentication method.
            var request = context.Transaction.GetHttpRequest();
            string? method = null;
            if (request is not null && !string.IsNullOrEmpty(context.Request.ClientSecret) &&
                string.IsNullOrEmpty(context.Request.ClientAssertion))
            {
                var header = request.Headers.Authorization.ToString();
                method = header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                    ? ClientAuthenticationMethods.ClientSecretBasic
                    : ClientAuthenticationMethods.ClientSecretPost;
            }
            if (method != policy.TokenEndpointAuthMethod)
                context.Reject(Errors.InvalidClient, "The client authentication method is not permitted for this client.");
        }
    }
}
