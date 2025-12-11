using Core.Application.Options;

namespace Core.Application.Utilities;

/// <summary>
/// Utility class for masking PII (Personally Identifiable Information) in audit logs.
/// </summary>
public static class PiiMasker
{
    private const string FullMask = "***";
    
    /// <summary>
    /// Masks an email address based on the specified masking level.
    /// </summary>
    /// <param name="email">The email to mask.</param>
    /// <param name="level">The masking level to apply.</param>
    /// <returns>The masked email string.</returns>
    public static string? MaskEmail(string? email, PiiMaskingLevel level)
    {
        if (string.IsNullOrWhiteSpace(email))
            return email;
            
        return level switch
        {
            PiiMaskingLevel.None => email,
            PiiMaskingLevel.Strict => FullMask,
            PiiMaskingLevel.Partial => MaskEmailPartial(email),
            _ => email
        };
    }
    
    /// <summary>
    /// Masks a name (first name, last name, or full name) based on the specified masking level.
    /// </summary>
    /// <param name="name">The name to mask.</param>
    /// <param name="level">The masking level to apply.</param>
    /// <returns>The masked name string.</returns>
    public static string? MaskName(string? name, PiiMaskingLevel level)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
            
        return level switch
        {
            PiiMaskingLevel.None => name,
            PiiMaskingLevel.Strict => FullMask,
            PiiMaskingLevel.Partial => MaskNamePartial(name),
            _ => name
        };
    }
    
    /// <summary>
    /// Masks a username based on the specified masking level.
    /// </summary>
    /// <param name="username">The username to mask.</param>
    /// <param name="level">The masking level to apply.</param>
    /// <returns>The masked username string.</returns>
    public static string? MaskUserName(string? username, PiiMaskingLevel level)
    {
        if (string.IsNullOrWhiteSpace(username))
            return username;
            
        return level switch
        {
            PiiMaskingLevel.None => username,
            PiiMaskingLevel.Strict => FullMask,
            PiiMaskingLevel.Partial => MaskUserNamePartial(username),
            _ => username
        };
    }
    
    /// <summary>
    /// Masks any generic PII value based on the specified masking level.
    /// </summary>
    /// <param name="value">The value to mask.</param>
    /// <param name="level">The masking level to apply.</param>
    /// <returns>The masked value string.</returns>
    public static string? MaskGeneric(string? value, PiiMaskingLevel level)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
            
        return level switch
        {
            PiiMaskingLevel.None => value,
            PiiMaskingLevel.Strict => FullMask,
            PiiMaskingLevel.Partial => MaskGenericPartial(value),
            _ => value
        };
    }
    
    private static string MaskEmailPartial(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return FullMask;
            
        var localPart = email[..atIndex];
        var domain = email[atIndex..];
        
        // Show first character + mask + domain
        if (localPart.Length <= 1)
            return $"{localPart}{FullMask}{domain}";
            
        return $"{localPart[0]}{FullMask}{domain}";
    }
    
    private static string MaskNamePartial(string name)
    {
        if (name.Length <= 1)
            return name;
            
        if (name.Length == 2)
            return $"{name[0]}*";
            
        // For Chinese names (2-4 chars): 王*明
        // For Western names: show first and last char
        return $"{name[0]}*{name[^1]}";
    }
    
    private static string MaskUserNamePartial(string username)
    {
        if (username.Length <= 2)
            return $"{username[0]}*";
            
        // Show first 2 chars + mask
        var showLength = Math.Min(3, username.Length / 2);
        return $"{username[..showLength]}{FullMask}";
    }
    
    private static string MaskGenericPartial(string value)
    {
        if (value.Length <= 2)
            return FullMask;
            
        // Show first and last character
        return $"{value[0]}{FullMask}{value[^1]}";
    }
}
