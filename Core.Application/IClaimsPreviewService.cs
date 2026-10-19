using Core.Application.DTOs;

namespace Core.Application;

public interface IClaimsPreviewService
{
    Task<ClaimsPreviewResponse> PreviewAsync(ClaimsPreviewRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClaimsPreviewClientDto>> GetClientsAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClaimsPreviewAccountDto>> GetAccountsAsync(string? search, int skip, int take, CancellationToken cancellationToken = default);
    Task<ClaimsPreviewClientScopesDto> GetScopesAsync(string clientId, CancellationToken cancellationToken = default);
}

public sealed class ClaimsPreviewException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
