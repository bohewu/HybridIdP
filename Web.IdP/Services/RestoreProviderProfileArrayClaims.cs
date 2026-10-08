using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Security;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Web.IdP.Services;

// IdentityModel flattens arrays during validation, losing [] and one-element shape.
// Restore only approved Profile arrays from the authenticated payload, never raw token text.
public sealed class RestoreProviderProfileArrayClaims(
    IApplicationDbContext db, IOptions<ProviderProfileOptions> options)
    : IOpenIddictServerHandler<ValidateTokenContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<RestoreProviderProfileArrayClaims>()
            .SetOrder(OpenIddictServerHandlers.Protection.ValidateIdentityModelToken.Descriptor.Order + 500)
            .Build();

    public async ValueTask HandleAsync(ValidateTokenContext context)
    {
        if (!options.Value.Enabled || context.Principal?.Identity is not ClaimsIdentity identity ||
            context.TokenValidationResult is not { IsValid: true, SecurityToken: JsonWebToken validated }) return;
        var token = validated.InnerToken ?? validated;
        var definitions = await db.ClaimDefinitions.AsNoTracking()
            .Where(def => def.DataType == "StringArray" && def.ProviderProfileSource != null && def.ConditionJson == null)
            .ToListAsync(context.CancellationToken);
        token.TryGetPayloadValue(OpenIddictConstants.Claims.Private.ClaimDestinationsMap,
            out Dictionary<string, string[]>? destinations);
        foreach (var definition in definitions)
        {
            if (ClaimSourcePropertyPolicy.IsProtectedProviderClaimType(definition.ClaimType) ||
                !options.Value.Sources.TryGetValue(definition.ProviderProfileSource!, out var source) ||
                !source.AllowedProperties.TryGetValue(definition.UserPropertyPath, out var type) || type != "StringArray") continue;
            var previous = identity.FindAll(definition.ClaimType).ToList();
            foreach (var claim in previous) identity.RemoveClaim(claim);
            if (!token.TryGetPayloadValue(definition.ClaimType, out JsonElement value) ||
                !ProviderProfileContract.IsValueOfType(value, "StringArray")) continue;
            var restored = new Claim(definition.ClaimType,
                ProviderProfileContract.NormalizeValue(value).GetRawText(), JsonClaimValueTypes.JsonArray);
            if (destinations?.TryGetValue(definition.ClaimType, out var targets) == true)
                restored.SetDestinations(targets);
            else if (previous.Count > 0) restored.SetDestinations(previous[0].GetDestinations());
            identity.AddClaim(restored);
        }
    }
}
