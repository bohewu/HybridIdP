namespace Core.Domain.Constants;

/// <summary>
/// Permission constants for fine-grained authorization
/// </summary>
public static class Permissions
{
    /// <summary>
    /// Client management permissions
    /// </summary>
    public static class Clients
    {
        public const string Read = "clients.read";
        public const string Create = "clients.create";
        public const string Update = "clients.update";
        public const string Delete = "clients.delete";
    }

    /// <summary>
    /// Scope management permissions
    /// </summary>
    public static class Scopes
    {
        public const string Read = "scopes.read";
        public const string Create = "scopes.create";
        public const string Update = "scopes.update";
        public const string Delete = "scopes.delete";
    }

    /// <summary>
    /// User management permissions
    /// </summary>
    public static class Users
    {
        public const string Read = "users.read";
        public const string Create = "users.create";
        public const string Update = "users.update";
        public const string Delete = "users.delete";
    }

    /// <summary>
    /// Role management permissions
    /// </summary>
    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Create = "roles.create";
        public const string Update = "roles.update";
        public const string Delete = "roles.delete";
    }

    /// <summary>
    /// Audit log permissions
    /// </summary>
    public static class Audit
    {
        public const string Read = "audit.read";
    }

    /// <summary>
    /// Settings management permissions
    /// </summary>
    public static class Settings
    {
        public const string Read = "settings.read";
        public const string Update = "settings.update";
    }

    /// <summary>
    /// Get all available permissions
    /// </summary>
    public static List<string> GetAll()
    {
        return new List<string>
        {
            Clients.Read, Clients.Create, Clients.Update, Clients.Delete,
            Scopes.Read, Scopes.Create, Scopes.Update, Scopes.Delete,
            Users.Read, Users.Create, Users.Update, Users.Delete,
            Roles.Read, Roles.Create, Roles.Update, Roles.Delete,
            Audit.Read,
            Settings.Read, Settings.Update
        };
    }
}
