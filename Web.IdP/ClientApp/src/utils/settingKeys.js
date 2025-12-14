/**
 * Well-known setting keys for type-safe access.
 * Mirrors Core.Domain.Constants.SettingKeys
 */

export const SettingKeys = {
    Branding: {
        AppName: "branding.appName",
        ProductName: "branding.productName"
    },
    Security: {
        PasswordMinLength: "security.password.minLength",
        PasswordRequireDigit: "security.password.requireDigit",
        PasswordRequireUppercase: "security.password.requireUppercase",
        PasswordRequireSpecialChar: "security.password.requireSpecialChar",
        RegistrationEnabled: "Security:RegistrationEnabled"
    },
    Email: {
        SmtpHost: "Mail.Host",
        SmtpPort: "Mail.Port",
        SmtpUsername: "Mail.Username",
        SmtpPassword: "Mail.Password",
        SmtpEnableSsl: "Mail.EnableSsl",
        FromAddress: "Mail.FromAddress",
        FromName: "Mail.FromName"
    },
    Audit: {
        RetentionDays: "Audit.RetentionDays"
    },
    Monitoring: {
        Enabled: "Monitoring.Enabled",
        ActivityIntervalSeconds: "Monitoring.ActivityIntervalSeconds",
        SecurityIntervalSeconds: "Monitoring.SecurityIntervalSeconds",
        MetricsIntervalSeconds: "Monitoring.MetricsIntervalSeconds"
    },
    Turnstile: {
        Enabled: "Turnstile.Enabled"
    }
}
