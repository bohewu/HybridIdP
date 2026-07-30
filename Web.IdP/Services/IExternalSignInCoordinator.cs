using Core.Application.DTOs;
using Core.Domain;

namespace Web.IdP.Services;

public enum ExternalSignInCompletionStatus
{
    Succeeded,
    TotpRequired,
    EmailOtpRequired,
    MfaEnrollmentRequired,
    Blocked
}

public sealed record ExternalSignInCompletionResult(
    ExternalSignInCompletionStatus Status,
    LoginResult? Denial = null)
{
    public bool IsSucceeded => Status == ExternalSignInCompletionStatus.Succeeded;

    public static ExternalSignInCompletionResult Succeeded() =>
        new(ExternalSignInCompletionStatus.Succeeded);

    public static ExternalSignInCompletionResult TotpRequired() =>
        new(ExternalSignInCompletionStatus.TotpRequired);

    public static ExternalSignInCompletionResult EmailOtpRequired() =>
        new(ExternalSignInCompletionStatus.EmailOtpRequired);

    public static ExternalSignInCompletionResult MfaEnrollmentRequired() =>
        new(ExternalSignInCompletionStatus.MfaEnrollmentRequired);

    public static ExternalSignInCompletionResult Blocked(LoginResult denial) =>
        new(ExternalSignInCompletionStatus.Blocked, denial);
}

public interface IExternalSignInCoordinator
{
    Task<ExternalSignInCompletionResult> CompleteAsync(
        HttpContext httpContext,
        ApplicationUser user,
        CancellationToken cancellationToken = default);
}
