namespace Core.Domain.Constants;

/// <summary>
/// Authentication and authorization constants used throughout the application.
/// Centralizes magic strings to prevent typos and improve maintainability.
/// </summary>
public static class AuthConstants
{
    /// <summary>
    /// Role names used for authorization.
    /// </summary>
    public static class Roles
    {
        /// <summary>
        /// Administrator role with full access to admin API and management features.
        /// </summary>
        public const string Admin = "Admin";
        
        /// <summary>
        /// Standard user role for regular authenticated users.
        /// </summary>
        public const string User = "User";
        
        /// <summary>
        /// Application Manager role - can manage OAuth clients and scopes they own.
        /// </summary>
        public const string ApplicationManager = "ApplicationManager";
    }

    /// <summary>
    /// Default admin account credentials for seeding.
    /// </summary>
    public static class DefaultAdmin
    {
        /// <summary>
        /// Default admin email/username.
        /// </summary>
        public const string Email = "admin@hybridauth.local";
        
        /// <summary>
        /// Default admin password. IMPORTANT: Change this in production!
        /// </summary>
        public const string Password = "Admin@123";
    }

    /// <summary>
    /// OpenID Connect scope names.
    /// </summary>
    public static class Scopes
    {
        public const string OpenId = "openid";
        public const string Email = "email";
        public const string Profile = "profile";
        public const string Roles = "roles";
    }

    /// <summary>
    /// Custom claim types used in the application.
    /// </summary>
    public static class Claims
    {
        /// <summary>
        /// Preferred username claim (maps to username or email).
        /// </summary>
        public const string PreferredUsername = "preferred_username";
        
        /// <summary>
        /// Department claim for organizational grouping.
        /// </summary>
        public const string Department = "department";
    }

    /// <summary>
    /// OpenIddict resource server identifiers.
    /// </summary>
    public static class Resources
    {
        public const string ResourceServer = "resource_server";
    }
}
