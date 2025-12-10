using System;
using System.Security.Cryptography;
using System.Text;

namespace Core.Application.Utilities;

/// <summary>
/// Utility class for hashing Personal Identification (PID) data.
/// Uses SHA256 for one-way hashing to protect sensitive identity documents.
/// </summary>
public static class PidHasher
{
    /// <summary>
    /// Computes SHA256 hash of the input value.
    /// Returns null if input is null or whitespace.
    /// </summary>
    /// <param name="value">The PID value to hash (e.g., National ID, Passport Number)</param>
    /// <returns>64-character hexadecimal hash string, or null if input is empty</returns>
    public static string? Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Normalize: trim and uppercase for consistent hashing
        var normalizedValue = value.Trim().ToUpperInvariant();
        
        var bytes = Encoding.UTF8.GetBytes(normalizedValue);
        var hashBytes = SHA256.HashData(bytes);
        
        return Convert.ToHexString(hashBytes); // Returns uppercase hex (64 chars)
    }

    /// <summary>
    /// Verifies if a plaintext value matches a stored hash.
    /// </summary>
    /// <param name="plaintext">The plaintext PID to verify</param>
    /// <param name="storedHash">The stored hash to compare against</param>
    /// <returns>True if the hash of plaintext matches the stored hash</returns>
    public static bool Verify(string? plaintext, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(plaintext) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var computedHash = Hash(plaintext);
        return string.Equals(computedHash, storedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a value appears to be already hashed (64-char hex string).
    /// </summary>
    public static bool IsHashed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // SHA256 hex hash is exactly 64 characters and all hex digits
        return value.Length == 64 && IsHexString(value);
    }

    private static bool IsHexString(string value)
    {
        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }
}
