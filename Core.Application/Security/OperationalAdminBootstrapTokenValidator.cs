using System.Security.Cryptography;
using System.Text;
using Core.Application.Options;

namespace Core.Application.Security;

public static class OperationalAdminBootstrapTokenValidator
{
    private const int Sha256ByteLength = 32;
    private const int Sha256HexLength = Sha256ByteLength * 2;
    private const int BootstrapTokenLength = 43;

    public static bool IsAuthorized(
        OperationalAdminBootstrapOptions options,
        string? presentedToken,
        DateTimeOffset utcNow)
    {
        if (!options.Enabled ||
            options.ExpiresAtUtc is not { } expiresAtUtc ||
            expiresAtUtc.Offset != TimeSpan.Zero ||
            expiresAtUtc <= utcNow.ToUniversalTime() ||
            string.IsNullOrEmpty(options.TokenSha256Digest) ||
            options.TokenSha256Digest.Length != Sha256HexLength ||
            !IsValidTokenFormat(presentedToken))
        {
            return false;
        }

        Span<byte> configuredDigest = stackalloc byte[Sha256ByteLength];
        if (!TryParseSha256Digest(
                options.TokenSha256Digest,
                configuredDigest))
        {
            CryptographicOperations.ZeroMemory(configuredDigest);
            return false;
        }

        var presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken!));
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                presentedDigest,
                configuredDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presentedDigest);
            CryptographicOperations.ZeroMemory(configuredDigest);
        }
    }

    private static bool IsValidTokenFormat(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != BootstrapTokenLength)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!(character is >= 'A' and <= 'Z') &&
                !(character is >= 'a' and <= 'z') &&
                !(character is >= '0' and <= '9') &&
                character is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSha256Digest(
        string hexDigest,
        Span<byte> destination)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            var high = HexValue(hexDigest[index * 2]);
            var low = HexValue(hexDigest[(index * 2) + 1]);
            if (high < 0 || low < 0)
            {
                CryptographicOperations.ZeroMemory(destination);
                return false;
            }

            destination[index] = (byte)((high << 4) | low);
        }

        return true;
    }

    private static int HexValue(char character) =>
        character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'A' and <= 'F' => character - 'A' + 10,
            >= 'a' and <= 'f' => character - 'a' + 10,
            _ => -1
        };
}
