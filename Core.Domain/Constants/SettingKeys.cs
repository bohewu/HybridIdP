namespace Core.Domain.Constants;

/// <summary>
/// Well-known setting keys for type-safe access.
/// </summary>
public static class SettingKeys
{
    public static class Branding
    {
        public const string AppName = "branding.appName";
        public const string ProductName = "branding.productName";
    }

    public static class Security
    {
        public const string PasswordMinLength = "security.password.minLength";
        public const string PasswordRequireDigit = "security.password.requireDigit";
        public const string PasswordRequireUppercase = "security.password.requireUppercase";
        public const string PasswordRequireSpecialChar = "security.password.requireSpecialChar";
    }

    public static class Email
    {
        public const string SmtpHost = "email.smtp.host";
        public const string SmtpPort = "email.smtp.port";
        public const string SmtpUsername = "email.smtp.username";
        public const string SmtpPassword = "email.smtp.password";
        public const string SmtpEnableSsl = "email.smtp.enableSsl";
        public const string FromAddress = "email.fromAddress";
        public const string FromName = "email.fromName";
    }
}
