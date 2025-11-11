using Core.Application;
using Core.Application.DTOs;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using System.Security.Cryptography;
using static OpenIddict.Abstractions.OpenIddictConstants;
using AuthConstants = Core.Domain.Constants.AuthConstants;

namespace Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly IOpenIddictApplicationManager _applicationManager;

    public ClientService(IOpenIddictApplicationManager applicationManager)
    {
        _applicationManager = applicationManager;
    }

    public async Task<(IEnumerable<ClientSummary> items, int totalCount)> GetClientsAsync(
        int skip,
        int take,
        string? search,
        string? type,
        string? sort)
    {
        var summaries = new List<ClientSummary>();

        await foreach (var application in _applicationManager.ListAsync())
        {
            var id = await _applicationManager.GetIdAsync(application);
            var clientId = await _applicationManager.GetClientIdAsync(application);
            var displayName = await _applicationManager.GetDisplayNameAsync(application);
            var clientType = await _applicationManager.GetClientTypeAsync(application);
            var consentType = await _applicationManager.GetConsentTypeAsync(application);
            var applicationType = await _applicationManager.GetApplicationTypeAsync(application);
            var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);

            summaries.Add(new ClientSummary
            {
                Id = id!,
                ClientId = clientId!,
                DisplayName = displayName,
                Type = clientType!,
                ApplicationType = applicationType!,
                ConsentType = consentType!,
                RedirectUrisCount = redirectUris.Count()
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

    public async Task<ClientDetail?> GetClientByIdAsync(Guid id)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString());
        if (application == null)
        {
            return null;
        }

        var redirectUris = await _applicationManager.GetRedirectUrisAsync(application);
        var postLogoutUris = await _applicationManager.GetPostLogoutRedirectUrisAsync(application);
        var permissions = await _applicationManager.GetPermissionsAsync(application);

        return new ClientDetail
        {
            Id = await _applicationManager.GetIdAsync(application) ?? string.Empty,
            ClientId = await _applicationManager.GetClientIdAsync(application) ?? string.Empty,
            DisplayName = await _applicationManager.GetDisplayNameAsync(application),
            Type = await _applicationManager.GetClientTypeAsync(application) ?? string.Empty,
            ConsentType = await _applicationManager.GetConsentTypeAsync(application) ?? string.Empty,
            ApplicationType = await _applicationManager.GetApplicationTypeAsync(application) ?? string.Empty,
            RedirectUris = redirectUris.Select(u => u.ToString()).ToList(),
            PostLogoutRedirectUris = postLogoutUris.Select(u => u.ToString()).ToList(),
            Permissions = permissions.ToList()
        };
    }

    public async Task<CreateClientResponse> CreateClientAsync(CreateClientRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(request));
        }

        // Check if client already exists
        var existing = await _applicationManager.FindByClientIdAsync(request.ClientId);
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

        // Add redirect URIs
        if (request.RedirectUris != null)
        {
            foreach (var uri in request.RedirectUris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var validUri))
                {
                    descriptor.RedirectUris.Add(validUri);
                }
            }
        }

        // Add post logout redirect URIs
        if (request.PostLogoutRedirectUris != null)
        {
            foreach (var uri in request.PostLogoutRedirectUris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var validUri))
                {
                    descriptor.PostLogoutRedirectUris.Add(validUri);
                }
            }
        }

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

        var application = await _applicationManager.CreateAsync(descriptor);
        var id = await _applicationManager.GetIdAsync(application);

        return new CreateClientResponse
        {
            Id = id ?? string.Empty,
            ClientId = request.ClientId,
            DisplayName = descriptor.DisplayName,
            ClientSecret = generatedSecret
        };
    }

    public async Task UpdateClientAsync(Guid id, UpdateClientRequest request)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString());
        if (application == null)
        {
            throw new KeyNotFoundException($"Client with ID '{id}' not found.");
        }

        // Get descriptor populated from existing application to preserve all properties
        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);

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
        }

        // Handle redirect URIs - replace if provided
        if (request.RedirectUris != null)
        {
            descriptor.RedirectUris.Clear();
            foreach (var uri in request.RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(uri));
            }
        }

        // Handle post logout redirect URIs - replace if provided
        if (request.PostLogoutRedirectUris != null)
        {
            descriptor.PostLogoutRedirectUris.Clear();
            foreach (var uri in request.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
            }
        }

        // Handle permissions - replace if provided
        if (request.Permissions != null)
        {
            descriptor.Permissions.Clear();
            foreach (var permission in request.Permissions)
            {
                descriptor.Permissions.Add(permission);
            }
        }

        await _applicationManager.PopulateAsync(application, descriptor);
        await _applicationManager.UpdateAsync(application);
    }

    public async Task DeleteClientAsync(Guid id)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString());
        if (application == null)
        {
            throw new KeyNotFoundException($"Client with ID '{id}' not found.");
        }

        await _applicationManager.DeleteAsync(application);
    }

    public async Task<string> RegenerateSecretAsync(Guid id)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString());
        if (application == null)
        {
            throw new KeyNotFoundException($"Client with ID '{id}' not found.");
        }

        var clientType = await _applicationManager.GetClientTypeAsync(application);
        if (clientType != ClientTypes.Confidential)
        {
            throw new InvalidOperationException("Secret regeneration is only available for confidential clients.");
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        var newSecret = Base64UrlTextEncoder.Encode(bytes);

        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = newSecret;

        await _applicationManager.PopulateAsync(application, descriptor);
        await _applicationManager.UpdateAsync(application);

        return newSecret;
    }
}
