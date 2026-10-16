namespace Core.Application.DTOs;

public sealed record AccountRecoveryCodeStatusDto(bool HasBatch, int RemainingCodes,
    int LegacyRemainingCodes = 0, bool HasLegacyBatch = false)
{
    public int TotalRemainingCodes => RemainingCodes + LegacyRemainingCodes;
}
