namespace Web.IdP.Attributes;

/// <summary>
/// Applies the shared cookie-aware antiforgery validation.
/// </summary>
public sealed class ValidateAntiforgeryForCookiesAttribute : ValidateCsrfForCookiesAttribute
{
}
