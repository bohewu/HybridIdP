namespace Core.Application;

/// <summary>
/// Raised when a caller attempts to modify a setting owned by the system.
/// </summary>
public sealed class SystemManagedSettingException : InvalidOperationException
{
    public SystemManagedSettingException()
        : base("System-managed settings cannot be modified.")
    {
    }
}
