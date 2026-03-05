namespace Core.Application.Options;

public class PrivilegedRoleProtectionOptions
{
    public const string Section = "PrivilegedRoleProtection";

    /// <summary>
    /// When true, the operator assigning privileged roles must have MFA enabled.
    /// </summary>
    public bool RequireOperatorMfaForPrivilegedRoleAssignment { get; set; } = false;

    /// <summary>
    /// When true, target users must have MFA enabled before privileged roles can be assigned.
    /// </summary>
    public bool RequireTargetMfaForPrivilegedRoleAssignment { get; set; } = false;

    /// <summary>
    /// Whether a registered passkey counts as MFA for this policy.
    /// </summary>
    public bool CountPasskeyAsMfa { get; set; } = true;

    /// <summary>
    /// Role names considered privileged.
    /// </summary>
    public string[] ProtectedRoles { get; set; } =
    [
        Core.Domain.Constants.AuthConstants.Roles.Admin,
        Core.Domain.Constants.AuthConstants.Roles.ApplicationManager
    ];
}
