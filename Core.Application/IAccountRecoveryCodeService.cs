using Core.Application.DTOs;
using Core.Domain;

namespace Core.Application;

public interface IAccountRecoveryCodeService
{
    Task<AccountRecoveryCodeStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>?> GenerateAsync(ApplicationUser user, CancellationToken ct = default);
    Task<bool> RedeemAsync(ApplicationUser user, string code, string operation, CancellationToken ct = default);
    // Scoped, post-commit evidence; never inferred from a caller-supplied method name.
    bool HasRedemptionProof(ApplicationUser user, string operation);
}
