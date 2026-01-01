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
        public const string Copyright = "branding.copyright";
        public const string PoweredBy = "branding.poweredBy";
    }

    public static class Security
    {
        public const string PasswordMinLength = "security.password.minLength";
        public const string PasswordRequireDigit = "security.password.requireDigit";
        public const string PasswordRequireUppercase = "security.password.requireUppercase";
        public const string PasswordRequireSpecialChar = "security.password.requireSpecialChar";
        public const string RegistrationEnabled = "Security:RegistrationEnabled";
    }

    public static class Email
    {
        public const string SmtpHost = "Mail.Host";
        public const string SmtpPort = "Mail.Port";
        public const string SmtpUsername = "Mail.Username";
        public const string SmtpPassword = "Mail.Password";
        public const string SmtpEnableSsl = "Mail.EnableSsl";
        public const string FromAddress = "Mail.FromAddress";
        public const string FromName = "Mail.FromName";
    }

    public static class Audit
    {
        public const string RetentionDays = "Audit.RetentionDays";
    }

    public static class Turnstile
    {
        public const string Enabled = "Turnstile.Enabled";
        public const string SiteKey = "Turnstile.SiteKey";
        public const string SecretKey = "Turnstile.SecretKey";
    }
}
