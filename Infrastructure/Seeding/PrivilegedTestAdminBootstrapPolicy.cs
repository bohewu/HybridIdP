namespace Infrastructure.Seeding;

public static class PrivilegedTestAdminBootstrapPolicy
{
    public const string ConfigurationKey = "SeedData:PrivilegedTestAdminBootstrap:Enabled";

    public static bool IsEnabled(bool explicitlyEnabled, string? environmentName)
    {
        if (!explicitlyEnabled)
        {
            return false;
        }

        return string.Equals(environmentName, "Development", StringComparison.Ordinal)
            || string.Equals(environmentName, "Test", StringComparison.Ordinal);
    }
}
