using System.Text.Json;
using Core.Domain;

namespace Core.Application.Ports;

public interface IProviderProfileService
{
    Task RefreshAfterLoginAsync(ApplicationUser user, string providerNamespace, string stableSubject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, JsonElement>> GetPropertiesAsync(ApplicationUser user, string source,
        CancellationToken cancellationToken = default);
}
