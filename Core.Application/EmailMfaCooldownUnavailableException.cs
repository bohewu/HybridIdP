namespace Core.Application;

public sealed class EmailMfaCooldownUnavailableException(Exception inner)
    : Exception("The email MFA send cooldown could not be confirmed.", inner);
