using System.Collections.Immutable;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web.IdP.Services; // For IScopeService if needed, or simply the namespace
using Core.Application; // For IApplicationDbContext
using Core.Application.Security;
using Core.Application.Utilities;
using IdentityModel;
using Microsoft.Extensions.Logging;

using UserAppRoleEntity = Core.Domain.Entities.UserAppRole;

namespace Web.IdP.Services;

public partial class ClaimsEnrichmentService : IClaimsEnrichmentService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<ClaimsEnrichmentService> _logger;
    private readonly IProviderProfileService? _profiles;
    private readonly ProviderProfileOptions _profileOptions;
    public const string ProfileClaimMarker = "oi_provider_profile_claims";

    public ClaimsEnrichmentService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IApplicationDbContext db,
        ILogger<ClaimsEnrichmentService> logger,
        IProviderProfileService? profiles = null, IOptions<ProviderProfileOptions>? profileOptions = null)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _logger = logger;
        _profiles = profiles;
        _profileOptions = profileOptions?.Value ?? new ProviderProfileOptions();
    }

    public Task AddPermissionClaimsAsync(ClaimsIdentity identity, ApplicationUser user, string? clientId = null, CancellationToken cancellationToken = default)
    {
        // User tokens carry application roles. IdP administration is cookie-only;
        // approved M2M capabilities are issued separately by the token service.
        return Task.CompletedTask;
    }

    public async Task AddScopeMappedClaimsAsync(ClaimsIdentity identity, ApplicationUser user, IEnumerable<string> grantedScopes, CancellationToken cancellationToken = default)
    {
        // Private ticket metadata permits removal even after a mapping is deleted or renamed.
        foreach (var marker in identity.FindAll(ProfileClaimMarker).ToList())
        {
            try
            {
                foreach (var type in JsonSerializer.Deserialize<string[]>(marker.Value) ?? [])
                    if (!ClaimSourcePropertyPolicy.IsProtectedClaimType(type))
                        foreach (var old in identity.FindAll(type).ToList()) identity.RemoveClaim(old);
            }
            catch (JsonException) { }
            identity.RemoveClaim(marker);
        }
        var profileClaimTypes = new HashSet<string>(StringComparer.Ordinal);
        var requestedScopes = grantedScopes.ToImmutableArray();
        if (requestedScopes.IsDefaultOrEmpty)
        {
            return;
        }

        var scopeNames = requestedScopes.ToArray(); // Materialize once
        LogEnrichingClaims(user.Id, string.Join(", ", scopeNames));
        
        var mappings = await _db.ScopeClaims
            .Include(sc => sc.ClaimDefinition)
            .Where(sc => scopeNames.Contains(sc.ScopeName))
            .ToListAsync(cancellationToken);

        LogFoundScopeMappings(mappings.Count);

        foreach (var map in mappings)
        {
            var def = map.ClaimDefinition;
            if (def == null) continue;
            if (ClaimSourcePropertyPolicy.IsProtectedClaimType(def.ClaimType)) continue;
            string? value;
            if (def.ProviderProfileSource is { } source)
            {
                if (ClaimSourcePropertyPolicy.IsProtectedProviderClaimType(def.ClaimType)) continue;
                if (_profiles is null || !_profileOptions.Enabled ||
                    !_profileOptions.Sources.TryGetValue(source, out var configured)) continue;
                var properties = await _profiles.GetPropertiesAsync(user, source, cancellationToken);
                if (def.ConditionJson is { } rule)
                {
                    ClaimCondition? condition;
                    try { condition = JsonSerializer.Deserialize<ClaimCondition>(rule); }
                    catch (JsonException) { continue; }
                    if (def.DataType != "Boolean" || condition is null ||
                        ClaimConditionPolicy.Evaluate(condition, configured.AllowedProperties, properties) is not { } known) continue;
                    value = known ? "true" : "false";
                }
                else
                {
                    if (!configured.AllowedProperties.TryGetValue(def.UserPropertyPath, out var type) ||
                        type != def.DataType || !properties.TryGetValue(def.UserPropertyPath, out var property) ||
                        !ProviderProfileContract.IsValueOfType(property, type)) continue;
                    value = type switch
                    {
                        "Boolean" => property.GetBoolean() ? "true" : "false",
                        "StringArray" => ProviderProfileContract.NormalizeValue(property).GetRawText(),
                        _ => property.GetString()
                    };
                }
            }
            else
            {
                if (def.DataType == "StringArray" || def.ConditionJson is not null || !ClaimSourcePropertyPolicy.TryResolve(user, def.UserPropertyPath, out var resolvedValue))
                {
                    LogRejectedClaimSource(def.ClaimType, def.UserPropertyPath);
                    continue;
                }
                value = def.ClaimType == OpenIddict.Abstractions.OpenIddictConstants.Claims.Name
                    ? NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName) ?? user.UserName
                    : resolvedValue;
            }
            // Log only which claim is being resolved, not the value
            LogResolvingClaim(def.ClaimType, def.UserPropertyPath);

            if (string.IsNullOrEmpty(value) && !map.AlwaysInclude)
            {
                LogSkippingEmptyClaim(def.ClaimType);
                continue;
            }

            if (identity.HasClaim(c => c.Type == def.ClaimType))
            {
                LogClaimExists(def.ClaimType);
                continue;
            }

            // Handle boolean types normalization if needed (e.g. email_verified)
            if (def.DataType == "Boolean" && bool.TryParse(value, out var boolVal))
            {
                 identity.AddClaim(new Claim(def.ClaimType, boolVal.ToString().ToLowerInvariant(), ClaimValueTypes.Boolean));
            }
            else if (def.DataType == "StringArray")
            {
                identity.AddClaim(new Claim(def.ClaimType, value!, Microsoft.IdentityModel.JsonWebTokens.JsonClaimValueTypes.JsonArray));
            }
            else
            {
                 identity.AddClaim(new Claim(def.ClaimType, value ?? string.Empty));
            }
            if (def.ProviderProfileSource is not null) profileClaimTypes.Add(def.ClaimType);
        }
        if (profileClaimTypes.Count > 0)
            identity.AddClaim(new Claim(ProfileClaimMarker, JsonSerializer.Serialize(profileClaimTypes)));
    }

    public async Task AddAppSpecificRolesAsync(ClaimsIdentity identity, ApplicationUser user, string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(clientId)) return;

        // Query App-Specific Roles for this user and client
        var appRoles = await _db.UserAppRoles
            .Where((UserAppRoleEntity r) => r.UserId == user.Id && r.ClientId == clientId)
            .ToListAsync(cancellationToken);

        if (appRoles.Count > 0)
        {
            LogFoundAppSpecificRoles(appRoles.Count, user.Id, clientId);
            
            foreach (var role in appRoles)
            {
                if (!string.IsNullOrWhiteSpace(role.RoleName))
                {
                    // Keep the explicit app_role claim as the authorization boundary marker.
                    identity.AddClaim(new Claim("app_role", role.RoleName));

                    // Preserve the standard role claim expected by downstream ASP.NET Core apps.
                    identity.AddClaim(new Claim(JwtClaimTypes.Role, role.RoleName));
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client {ClientId} is not privileged. Skipping IdP permission claims.")]
    private partial void LogClientNotPrivileged(string? clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enriching claims for user {UserId} with scopes: {Scopes}")]
    private partial void LogEnrichingClaims(Guid userId, string scopes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} scope mappings for requested scopes.")]
    private partial void LogFoundScopeMappings(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolving claim {ClaimType} from path {Path}.")]
    private partial void LogResolvingClaim(string claimType, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping empty claim {ClaimType} (AlwaysInclude=false)")]
    private partial void LogSkippingEmptyClaim(string claimType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Claim {ClaimType} already exists in identity. Skipping.")]
    private partial void LogClaimExists(string claimType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping claim {ClaimType} because source path {Path} is not approved for token issuance.")]
    private partial void LogRejectedClaimSource(string claimType, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} app-specific roles for user {UserId} and client {ClientId}.")]
    private partial void LogFoundAppSpecificRoles(int count, Guid userId, string clientId);
}
