namespace Core.Application.DTOs;

public sealed record PasskeyRegistrationResult(bool Success, string? Error, bool UserVerified,
    IReadOnlyList<string>? AccountRecoveryCodes = null)
{
    public static implicit operator PasskeyRegistrationResult((bool Success, string? Error, bool UserVerified) value) =>
        new(value.Success, value.Error, value.UserVerified);
}
