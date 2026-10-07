using Core.Domain.Entities;
using OpenIddict.Abstractions;

namespace Infrastructure.Authorization;

public sealed record ApiUsageActor(Guid? UserId, Guid? PersonId, bool IsAdmin, bool IsBearer);

public interface IApiScopeUsagePolicy
{
    Task<ApiUsageActor> GetActorAsync(CancellationToken cancellationToken = default);
    Task RequireResourceAuthorityAsync(ApiResource resource, string permission, CancellationToken cancellationToken = default);
    Task RequireScopeMappingAuthorityAsync(IEnumerable<string> scopeIds, CancellationToken cancellationToken = default);
    Task PrepareClientScopesAsync(OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken = default);
    Task<bool> CanUseScopesAsync(object application, IEnumerable<string> scopes, CancellationToken cancellationToken = default);
    Task ApproveAsync(Guid applicationId, string scopeId, int? resourceId, CancellationToken cancellationToken = default);
    Task<bool> CanViewScopeAsync(string scopeId, CancellationToken cancellationToken = default);
}
