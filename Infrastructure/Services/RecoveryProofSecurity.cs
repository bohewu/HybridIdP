using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services;

internal static class RecoveryProofSecurity
{
    public static string GenerateNumericCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static string GenerateOpaqueValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string BindToContext(
        string secret,
        string contextHash,
        string csrfHash,
        string recoveryEmailBinding) =>
        $"{secret.Length}:{secret}{contextHash.Length}:{contextHash}{csrfHash.Length}:{csrfHash}" +
        $"{recoveryEmailBinding.Length}:{recoveryEmailBinding}";

    public static bool TryNormalizeAddress(string candidate, out string address, out string normalizedAddress)
    {
        address = string.Empty;
        normalizedAddress = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 320 || !MailAddress.TryCreate(candidate.Trim(), out var parsed))
        {
            return false;
        }

        address = parsed.Address;
        if (!string.Equals(address, candidate.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            address = string.Empty;
            return false;
        }

        normalizedAddress = address.ToUpperInvariant();
        return true;
    }

    public static string MaskAddress(string address)
    {
        var separator = address.IndexOf('@');
        if (separator <= 0)
        {
            return "***";
        }

        var local = address[..separator];
        var visible = local[..1];
        return $"{visible}***{address[separator..]}";
    }
}
