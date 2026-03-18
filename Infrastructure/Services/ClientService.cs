using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Core.Domain.Events;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Security.Cryptography;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Text.Json;
using System.Threading;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using AuthConstants = Core.Domain.Constants.AuthConstants;

namespace Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IApplicationDbContext _context;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly RedirectUriSecurityPolicyOptions _redirectUriSecurityPolicy;
    private readonly HashSet<string> _allowedRedirectHosts;

    public ClientService(
        IOpenIddictApplicationManager applicationManager, 
        IDomainEventPublisher eventPublisher,
        IApplicationDbContext context,
        IOpenIddictScopeManager scopeManager,
        IOptions<RedirectUriSecurityPolicyOptions> redirectUriSecurityPolicyOptions)
    {
        _applicationManager = applicationManager;
        _eventPublisher = eventPublisher;
        _context = context;
        _scopeManager = scopeManager;
        _redirectUriSecurityPolicy = redirectUriSecurityPolicyOptions.Value;
        _allowedRedirectHosts = (_redirectUriSecurityPolicy.AllowedHosts ?? [])
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(IEnumerable<ClientSummary> items, int totalCount)> GetClientsAsync(
        int skip,
        int take,
        string? search,
        string? type,
        string? sort,
        Guid? ownerPersonId = null,
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<ClientSummary>();

        // Get owned client IDs if filtering by owner
        HashSet<string>? ownedClientIds = null;
        if (ownerPersonId.HasValue)
        {
            ownedClientIds = (await _context.ClientOwnerships
                .Where(co => co.CreatedByPersonId == ownerPersonId.Value)
                .Select(co => co.ClientId)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        await foreach (var application in _applicationManager.ListAsync().WithCancellation(cancellationToken))
        {
            var id = await _applicationManager.GetIdAsync(application);
            var clientId = await _applicationManager.GetClientIdAsync(application);
            
            // Skip if filtering by owner and this client is not owned
            if (ownedClientIds != null && !ownedClientIds.Contains(clientId!))
            {
                continue;
            }
            
            var displayName = await _applicationManager.GetDisplayNameAsync(application);
            var clientType = await _applicationManager.GetClientTypeAsync(application);
            var consentType = await _applicationManager.GetConsentTypeAsync(application);
            var applicationType = await _applicationManager.GetApplicationTypeAsync(application);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);
            var properties = await _applicationManager.GetPropertiesAsync(application);

            summaries.Add(new ClientSummary
            {
                Id = id!,
                ClientId = clientId!,
                DisplayName = displayName,
                Type = clientType!,
                ApplicationType = applicationType!,
                ConsentType = consentType!,
                RedirectUrisCount = redirectUris.Length,
                SupportedRoles = GetSupportedRoles(properties)
            });
        }

        // Filtering
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            summaries = summaries.Where(x =>
                (!string.IsNullOrEmpty(x.ClientId) && x.ClientId.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.DisplayName) && x.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim().ToLowerInvariant();
            summaries = summaries.Where(x => string.Equals(x.Type, t, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Sorting
        string sortField = "clientId";
        bool sortAsc = true;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var parts = sort.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                sortField = parts[0].ToLowerInvariant();
            }
            if (parts.Length > 1)
            {
                sortAsc = !string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase);
            }
        }

        Func<ClientSummary, object?> keySelector = sortField switch
        {
            "displayname" => x => x.DisplayName,
            "type" => x => x.Type,
            "redirecturiscnt" => x => x.RedirectUrisCount,
            "redirecturicount" => x => x.RedirectUrisCount,
            _ => x => x.ClientId
        };

        summaries = (sortAsc
            ? summaries.OrderBy(keySelector)
            : summaries.OrderByDescending(keySelector)).ToList();

        var totalCount = summaries.Count;

        // Paging safety
        if (skip < 0) skip = 0;
        if (take <= 0) take = 25;

        var items = summaries.Skip(skip).Take(take).ToList();

        return (items, totalCount);
    }

    public async Task<ClientDetail?> GetClientByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString(), cancellationToken);
        if (application == null)
        {
            return null;
        }

        var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);
        var postLogoutUris = await _applicationManager.GetPostLogoutRedirectUrisAsync(application);
        var permissions = await _applicationManager.GetPermissionsAsync(application);
        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        var clientType = await _applicationManager.GetClientTypeAsync(application) ?? string.Empty;
        var properties = await _applicationManager.GetPropertiesAsync(application);

        return new ClientDetail
        {
            Id = await _applicationManager.GetIdAsync(application) ?? string.Empty,
            ClientId = await _applicationManager.GetClientIdAsync(application) ?? string.Empty,
            DisplayName = await _applicationManager.GetDisplayNameAsync(application),
            Type = clientType,
            ConsentType = await _applicationManager.GetConsentTypeAsync(application) ?? string.Empty,
            ApplicationType = await _applicationManager.GetApplicationTypeAsync(application) ?? string.Empty,
            RedirectUris = redirectUris.Select(u => u.ToString()).ToList(),
            PostLogoutRedirectUris = postLogoutUris.Select(u => u.ToString()).ToList(),
            Permissions = permissions.ToList(),
            SupportedRoles = GetSupportedRoles(properties),
            RequirePkce = clientType == ClientTypes.Public || descriptor.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange),
            DisableExternalProviders = GetDisableExternalProviders(properties),
            EnableTurnstile = GetEnableTurnstile(properties)
        };
    }

    public async Task<CreateClientResponse> CreateClientAsync(CreateClientRequest request, Guid? creatorPersonId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(request));
        }

        // Check if client already exists
        var existing = await _applicationManager.FindByClientIdAsync(request.ClientId, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"Client with ID '{request.ClientId}' already exists.");
        }

        string? generatedSecret = null;
        var clientSecret = request.ClientSecret;

        // Determine client type - use provided or infer from secret
        var clientType = request.Type;
        if (string.IsNullOrEmpty(clientType))
        {
            // If not specified, infer from secret presence
            clientType = string.IsNullOrEmpty(request.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential;
        }

        // Validate client type and secret combination FIRST
        if (clientType == ClientTypes.Public && !string.IsNullOrEmpty(request.ClientSecret))
        {
            throw new ArgumentException("Public clients should not have a ClientSecret. Remove the secret or select Confidential client type.");
        }

        // Handle secret generation for confidential clients
        if (clientType == ClientTypes.Confidential)
        {
            if (string.IsNullOrEmpty(request.ClientSecret))
            {
                // Generate a new secret for confidential clients if not provided
                var bytes = RandomNumberGenerator.GetBytes(32);
                generatedSecret = Base64UrlTextEncoder.Encode(bytes);
                clientSecret = generatedSecret;
            }
            else
            {
                // User provided a secret
                clientSecret = request.ClientSecret;
            }
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = request.ClientId,
            ClientSecret = clientSecret,
            DisplayName = request.DisplayName ?? request.ClientId,
            ConsentType = request.ConsentType ?? ConsentTypes.Explicit,
            ApplicationType = request.ApplicationType ?? ApplicationTypes.Web,  // Default to web if not specified
            ClientType = clientType
        };

        ApplyPkceRequirement(descriptor, clientType, request.RequirePkce, defaultConfidentialRequirePkce: true);

        // Add Support Roles
        if (request.SupportedRoles != null && request.SupportedRoles.Any())
        {
             descriptor.Properties[AuthConstants.Properties.SupportedRoles] = 
                 JsonSerializer.SerializeToElement(request.SupportedRoles);
        }

        if (request.DisableExternalProviders == true)
        {
            descriptor.Properties[AuthConstants.Properties.DisableExternalProviders] = JsonSerializer.SerializeToElement(true);
        }

        if (request.EnableTurnstile == true)
        {
            descriptor.Properties[AuthConstants.Properties.EnableTurnstile] = JsonSerializer.SerializeToElement(true);
        }

        // Validate Native app constraints
        if (descriptor.ApplicationType == ApplicationTypes.Native && descriptor.ClientType != ClientTypes.Public)
        {
            throw new ArgumentException("Native applications must be public clients (cannot have a client secret).");
        }

        ApplyValidatedRedirectUris(descriptor.RedirectUris, request.RedirectUris, nameof(request.RedirectUris));
        ApplyValidatedRedirectUris(descriptor.PostLogoutRedirectUris, request.PostLogoutRedirectUris, nameof(request.PostLogoutRedirectUris));

        // Add permissions
        if (request.Permissions != null)
        {
            foreach (var permission in request.Permissions)
            {
                descriptor.Permissions.Add(permission);
            }
        }
        else
        {
            // Default permissions for authorization code flow
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Email);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Profile);
            descriptor.Permissions.Add($"{OpenIddictConstants.Permissions.Prefixes.Scope}{AuthConstants.Scopes.OpenId}");
        }
        
        // Auto-add response_type permissions based on grant types
        if (descriptor.Permissions.Contains(Permissions.GrantTypes.AuthorizationCode) ||
            descriptor.Permissions.Contains(Permissions.GrantTypes.Implicit))
        {
            // Authorization code and implicit flows need response_type:code
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        }
        
        if (descriptor.Permissions.Contains(Permissions.GrantTypes.Implicit))
        {
            // Implicit flow also needs response_type:token and id_token
            descriptor.Permissions.Add(Permissions.ResponseTypes.Token);
            descriptor.Permissions.Add(Permissions.ResponseTypes.IdToken);
        }

        // Phase 13.2: Validate M2M clients cannot request public (OIDC) scopes
        var isM2MClient = descriptor.Permissions.Contains(Permissions.GrantTypes.ClientCredentials);
        if (isM2MClient)
        {
            // Get all requested scopes
            var requestedScopes = descriptor.Permissions
                .Where(p => p.StartsWith(Permissions.Prefixes.Scope))
                .Select(p => p.Substring(Permissions.Prefixes.Scope.Length))
                .ToList();

            // Check if any requested scope is public
            foreach (var scopeName in requestedScopes)
            {
                var scope = await _scopeManager.FindByNameAsync(scopeName, cancellationToken);
                if (scope != null)
                {
                    var scopeId = await _scopeManager.GetIdAsync(scope, cancellationToken);
                    var extension = await _context.ScopeExtensions
                        .FirstOrDefaultAsync(se => se.ScopeId == scopeId, cancellationToken);
                    
                    if (extension?.IsPublic == true)
                    {
                        throw new InvalidOperationException(
                            $"M2M clients (client_credentials grant) cannot request public scope '{scopeName}'. " +
                            "Public scopes are user-centric (openid, profile, email, roles) and should not be used by service accounts.");
                    }
                }
            }
        }

        // Validate Redirect URIs for interactive clients
        if ((descriptor.Permissions.Contains(Permissions.GrantTypes.AuthorizationCode) ||
             descriptor.Permissions.Contains(Permissions.GrantTypes.Implicit)) &&
            descriptor.RedirectUris.Count == 0)
        {
            throw new ArgumentException("Redirect URIs are required for interactive clients (Authorization Code or Implicit flow).");
        }

        var application = await _applicationManager.CreateAsync(descriptor, cancellationToken);
        var id = await _applicationManager.GetIdAsync(application, cancellationToken);

        // Create ownership record if creator info provided
        if (creatorPersonId.HasValue)
        {
            var ownership = new ClientOwnership
            {
                ClientId = request.ClientId,
                CreatedByPersonId = creatorPersonId.Value,
                CreatedAt = DateTime.UtcNow
            };
            _context.ClientOwnerships.Add(ownership);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Publish domain event
        await _eventPublisher.PublishAsync(new ClientCreatedEvent(id!, request.ClientId));

        return new CreateClientResponse
        {
            Id = id ?? string.Empty,
            ClientId = request.ClientId,
            DisplayName = descriptor.DisplayName,
            ClientSecret = generatedSecret
        };
    }

    public async Task UpdateClientAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken = default)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString(), cancellationToken);
        if (application == null)
        {
            throw new KeyNotFoundException($"Client with ID '{id}' not found.");
        }

        // Get descriptor populated from existing application to preserve all properties
        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application, cancellationToken);

        // Ensure ApplicationType and ClientType are set (fix for existing apps without type)
        if (string.IsNullOrEmpty(descriptor.ApplicationType))
        {
            descriptor.ApplicationType = ApplicationTypes.Web;
        }

        if (string.IsNullOrEmpty(descriptor.ClientType))
        {
            // Determine based on whether there's currently a secret
            var hasSecret = !string.IsNullOrEmpty(descriptor.ClientSecret);
            descriptor.ClientType = hasSecret ? ClientTypes.Confidential : ClientTypes.Public;
        }

        var effectiveRequirePkce = descriptor.ClientType == ClientTypes.Public
            ? true
            : request.RequirePkce ?? descriptor.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange);

        // Update only the fields provided in the request
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            descriptor.ClientId = request.ClientId;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            descriptor.DisplayName = request.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(request.ConsentType))
        {
            descriptor.ConsentType = request.ConsentType;
        }



        // Only set ClientSecret if a new one is explicitly provided
        if (!string.IsNullOrEmpty(request.ClientSecret))
        {
            descriptor.ClientSecret = request.ClientSecret;
            descriptor.ClientType = ClientTypes.Confidential;  // Update type if adding/changing secret
            effectiveRequirePkce = request.RequirePkce ?? descriptor.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange);
        }

        ApplyPkceRequirement(descriptor, descriptor.ClientType, effectiveRequirePkce, defaultConfidentialRequirePkce: true);

        // Validate Native app constraints
        if (descriptor.ApplicationType == ApplicationTypes.Native && descriptor.ClientType != ClientTypes.Public)
        {
            throw new ArgumentException("Native applications must be public clients (cannot have a client secret).");
        }

        ApplyValidatedRedirectUris(descriptor.RedirectUris, request.RedirectUris, nameof(request.RedirectUris));
        ApplyValidatedRedirectUris(descriptor.PostLogoutRedirectUris, request.PostLogoutRedirectUris, nameof(request.PostLogoutRedirectUris));

        // Handle permissions - replace if provided
        if (request.Permissions != null)
        {
            descriptor.Permissions.Clear();
            foreach (var permission in request.Permissions)
            {
                descriptor.Permissions.Add(permission);
            }
            
            // Auto-add response_type permissions based on grant types
            if (descriptor.Permissions.Contains(Permissions.GrantTypes.AuthorizationCode) ||
                descriptor.Permissions.Contains(Permissions.GrantTypes.Implicit))
            {
                // Authorization code and implicit flows need response_type:code
                descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            }
            
            if (descriptor.Permissions.Contains(Permissions.GrantTypes.Implicit))
            {
                // Implicit flow also needs response_type:token and id_token
                descriptor.Permissions.Add(Permissions.ResponseTypes.Token);
                descriptor.Permissions.Add(Permissions.ResponseTypes.IdToken);
            }

            // Phase 13.2: Validate M2M clients cannot request public (OIDC) scopes
            var isM2MClient = descriptor.Permissions.Contains(Permissions.GrantTypes.ClientCredentials);
            if (isM2MClient)
            {
                // Get all requested scopes
                var requestedScopes = descriptor.Permissions
                    .Where(p => p.StartsWith(Permissions.Prefixes.Scope))
                    .Select(p => p.Substring(Permissions.Prefixes.Scope.Length))
                    .ToList();

                // Check if any requested scope is public
                foreach (var scopeName in requestedScopes)
                {
                    var scope = await _scopeManager.FindByNameAsync(scopeName, cancellationToken);
                    if (scope != null)
                    {
                        var scopeId = await _scopeManager.GetIdAsync(scope, cancellationToken);
                        var extension = await _context.ScopeExtensions
                            .FirstOrDefaultAsync(se => se.ScopeId == scopeId, cancellationToken);
                        
                        if (extension?.IsPublic == true)
                        {
                            throw new InvalidOperationException(
                                $"M2M clients (client_credentials grant) cannot request public scope '{scopeName}'. " +
                                "Public scopes are user-centric (openid, profile, email, roles) and should not be used by service accounts.");
                        }
                    }
                }
            }

            // Publish scope change event if permissions include scopes
            // Publish scope change event if permissions include scopes
            var scopeChanges = string.Join(", ", request.Permissions.Where(p => p.StartsWith(Permissions.Prefixes.Scope)));
            if (!string.IsNullOrEmpty(scopeChanges))
            {
                await _eventPublisher.PublishAsync(new ClientScopeChangedEvent(id.ToString(), descriptor.ClientId!, scopeChanges));
            }
        }

        // Handle SupportedRoles
        if (request.SupportedRoles != null)
        {
            if (request.SupportedRoles.Any())
            {
                descriptor.Properties[AuthConstants.Properties.SupportedRoles] = 
                    JsonSerializer.SerializeToElement(request.SupportedRoles);
            }
            else
            {
                descriptor.Properties.Remove(AuthConstants.Properties.SupportedRoles);
            }
        }

        if (request.DisableExternalProviders != null)
        {
            if (request.DisableExternalProviders == true)
            {
                descriptor.Properties[AuthConstants.Properties.DisableExternalProviders] = JsonSerializer.SerializeToElement(true);
            }
            else
            {
                descriptor.Properties.Remove(AuthConstants.Properties.DisableExternalProviders);
            }
        }

        if (request.EnableTurnstile != null)
        {
            if (request.EnableTurnstile == true)
            {
                descriptor.Properties[AuthConstants.Properties.EnableTurnstile] = JsonSerializer.SerializeToElement(true);
            }
            else
            {
                descriptor.Properties.Remove(AuthConstants.Properties.EnableTurnstile);
            }
        }

        // Validate Redirect URIs for interactive clients
        if ((descriptor.Permissions.Contains(Permissions.GrantTypes.AuthorizationCode) ||
             descriptor.Permissions.Contains(Permissions.GrantTypes.Implicit)) &&
            descriptor.RedirectUris.Count == 0)
        {
            throw new ArgumentException("Redirect URIs are required for interactive clients (Authorization Code or Implicit flow).");
        }

        await _applicationManager.PopulateAsync(application, descriptor, cancellationToken);
        await _applicationManager.UpdateAsync(application, cancellationToken);

        // Publish domain event
        var changes = "Updated client details";
        await _eventPublisher.PublishAsync(new ClientUpdatedEvent(id.ToString(), descriptor.ClientId!, changes));

        // If secret was changed, publish separate event
        if (!string.IsNullOrEmpty(request.ClientSecret))
        {
            await _eventPublisher.PublishAsync(new ClientSecretChangedEvent(id.ToString(), descriptor.ClientId!));
        }
    }

    public async Task DeleteClientAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString(), cancellationToken);
        if (application == null)
        {
            throw new KeyNotFoundException($"Client with ID '{id}' not found.");
        }

        var clientId = await _applicationManager.GetClientIdAsync(application, cancellationToken);

        await _applicationManager.DeleteAsync(application, cancellationToken);

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var ownerships = _context.ClientOwnerships
                .Where(co => co.ClientId == clientId);

            _context.ClientOwnerships.RemoveRange(ownerships);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Publish domain event
        await _eventPublisher.PublishAsync(new ClientDeletedEvent(id.ToString(), clientId!));
    }

    public async Task<string> RegenerateSecretAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString(), cancellationToken);
        if (application == null)
        {
            throw new KeyNotFoundException($"Client with ID '{id}' not found.");
        }

        var clientType = await _applicationManager.GetClientTypeAsync(application, cancellationToken);
        if (clientType != ClientTypes.Confidential)
        {
            throw new InvalidOperationException("Secret regeneration is only available for confidential clients.");
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        var newSecret = Base64UrlTextEncoder.Encode(bytes);

        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.ClientSecret = newSecret;

        await _applicationManager.PopulateAsync(application, descriptor, cancellationToken);
        await _applicationManager.UpdateAsync(application, cancellationToken);

        // Publish domain event
        var clientId = await _applicationManager.GetClientIdAsync(application, cancellationToken);
        await _eventPublisher.PublishAsync(new ClientSecretChangedEvent(id.ToString(), clientId!));

        return newSecret;
    }

    public async Task<bool> IsClientOwnedByPersonAsync(Guid clientId, Guid personId, CancellationToken cancellationToken = default)
    {
        // First get the clientId string from the application
        var application = await _applicationManager.FindByIdAsync(clientId.ToString(), cancellationToken);
        if (application == null)
        {
            return false;
        }

        var clientIdStr = await _applicationManager.GetClientIdAsync(application, cancellationToken);
        if (string.IsNullOrEmpty(clientIdStr))
        {
            return false;
        }

        return await _context.ClientOwnerships
            .AnyAsync(co => co.ClientId == clientIdStr && co.CreatedByPersonId == personId, cancellationToken);
    }

    private static List<string> GetSupportedRoles(System.Collections.Immutable.ImmutableDictionary<string, JsonElement>? properties)
    {
        if (properties is null)
        {
            return new();
        }

        if (properties.TryGetValue(AuthConstants.Properties.SupportedRoles, out var element) &&
            element.ValueKind == JsonValueKind.Array)
        {
            return element.Deserialize<List<string>>() ?? new();
        }
        return new();
    }

    private static bool GetDisableExternalProviders(System.Collections.Immutable.ImmutableDictionary<string, JsonElement>? properties)
    {
        if (properties is null)
        {
            return false;
        }

        if (!properties.TryGetValue(AuthConstants.Properties.DisableExternalProviders, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static bool GetEnableTurnstile(System.Collections.Immutable.ImmutableDictionary<string, JsonElement>? properties)
    {
        if (properties is null)
        {
            return false;
        }

        if (!properties.TryGetValue(AuthConstants.Properties.EnableTurnstile, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static void ApplyPkceRequirement(
        OpenIddictApplicationDescriptor descriptor,
        string? clientType,
        bool? requirePkce,
        bool defaultConfidentialRequirePkce)
    {
        var shouldRequirePkce = string.Equals(clientType, ClientTypes.Public, StringComparison.Ordinal)
            ? true
            : requirePkce ?? defaultConfidentialRequirePkce;

        descriptor.Requirements.Remove(Requirements.Features.ProofKeyForCodeExchange);
        if (shouldRequirePkce)
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }
    }

    private void ApplyValidatedRedirectUris(
        ICollection<Uri> targetUris,
        IEnumerable<string>? requestedUris,
        string argumentName)
    {
        if (requestedUris == null)
        {
            return;
        }

        targetUris.Clear();
        foreach (var requestedUri in requestedUris)
        {
            targetUris.Add(ParseAndValidateRedirectUri(requestedUri, argumentName));
        }
    }

    private Uri ParseAndValidateRedirectUri(string requestedUri, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(requestedUri))
        {
            throw new ArgumentException($"{argumentName} contains an empty URI.", argumentName);
        }

        if (!Uri.TryCreate(requestedUri, UriKind.Absolute, out var parsedUri))
        {
            throw new ArgumentException($"'{requestedUri}' is not a valid absolute URI.", argumentName);
        }

        if (_redirectUriSecurityPolicy.EnforceHttps &&
            string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !(_redirectUriSecurityPolicy.AllowLocalhostHttp && parsedUri.IsLoopback))
        {
            throw new ArgumentException(
                $"HTTP redirect URI '{requestedUri}' is not allowed by policy.",
                argumentName);
        }

        if (_allowedRedirectHosts.Count > 0 && !_allowedRedirectHosts.Contains(parsedUri.Host))
        {
            throw new ArgumentException(
                $"Host '{parsedUri.Host}' is not allowed by redirect URI policy.",
                argumentName);
        }

        return parsedUri;
    }
}
