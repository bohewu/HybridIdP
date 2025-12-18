namespace Infrastructure.Validators;

/// <summary>
/// Static validator class for identity documents (Phase 10.6).
/// Implements validation logic for Taiwan National IDs, Passports, and Resident Certificates.
/// </summary>
public static class IdentityDocumentValidator
{
    /// <summary>
    /// Letter-to-number mapping for Taiwan National ID checksum validation.
    /// Reference: https://zh.wikipedia.org/wiki/中華民國國民身分證
    /// </summary>
    private static readonly Dictionary<char, int> LetterValues = new()
    {
        {'A', 10}, {'B', 11}, {'C', 12}, {'D', 13}, {'E', 14}, {'F', 15},
        {'G', 16}, {'H', 17}, {'I', 34}, {'J', 18}, {'K', 19}, {'L', 20},
        {'M', 21}, {'N', 22}, {'O', 35}, {'P', 23}, {'Q', 24}, {'R', 25},
        {'S', 26}, {'T', 27}, {'U', 28}, {'V', 29}, {'W', 32}, {'X', 30},
        {'Y', 31}, {'Z', 33}
    };

    /// <summary>
    /// Validates a Taiwan ROC National ID with full checksum algorithm.
    /// Format: 1 letter + 9 digits (e.g., A123456789)
    /// </summary>
    /// <param name="nationalId">The national ID to validate</param>
    /// <returns>True if the national ID is valid; otherwise, false</returns>
    public static bool IsValidTaiwanNationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId)) 
            return false;

        nationalId = nationalId.Trim().ToUpperInvariant();

        // Format check: 1 letter + 9 digits
        if (nationalId.Length != 10) 
            return false;
        if (!char.IsLetter(nationalId[0])) 
            return false;
        if (!nationalId.Substring(1).All(char.IsDigit)) 
            return false;

        // Letter must be valid (A-Z)
        if (!LetterValues.TryGetValue(nationalId[0], out int letterValue)) 
            return false;
        int[] weights = { 1, 9, 8, 7, 6, 5, 4, 3, 2, 1 }; // 10 weights total

        // Convert letter to two digits (e.g., A=10 → [1, 0])
        int firstDigit = letterValue / 10;
        int secondDigit = letterValue % 10;

        // Calculate checksum
        int sum = firstDigit * weights[0] + secondDigit * weights[1];

        for (int i = 1; i < 9; i++)
        {
            sum += (nationalId[i] - '0') * weights[i + 1];
        }

        int lastDigit = nationalId[9] - '0';
        int checksum = (10 - (sum % 10)) % 10;

        return checksum == lastDigit;
    }

    /// <summary>
    /// Validates a passport number.
    /// International standard: 6-12 alphanumeric characters.
    /// </summary>
    /// <param name="passportNumber">The passport number to validate</param>
    /// <returns>True if the passport number is valid; otherwise, false</returns>
    public static bool IsValidPassportNumber(string? passportNumber)
    {
        if (string.IsNullOrWhiteSpace(passportNumber)) 
            return false;

        passportNumber = passportNumber.Trim().ToUpperInvariant();

        // Taiwan passport format: 9 alphanumeric characters (e.g., 300123456)
        // International standard: 6-12 characters
        if (passportNumber.Length < 6 || passportNumber.Length > 12) 
            return false;

        return passportNumber.All(c => char.IsLetterOrDigit(c));
    }

    /// <summary>
    /// Validates a resident certificate number (for foreign residents in Taiwan).
    /// Format: Similar to National ID - 10-12 alphanumeric characters.
    /// </summary>
    /// <param name="residentCert">The resident certificate number to validate</param>
    /// <returns>True if the resident certificate number is valid; otherwise, false</returns>
    public static bool IsValidResidentCertificateNumber(string? residentCert)
    {
        if (string.IsNullOrWhiteSpace(residentCert)) 
            return false;

        residentCert = residentCert.Trim().ToUpperInvariant();

        // Taiwan Resident Certificate format: Similar to National ID
        // Format: 2 letters + 8 digits (e.g., AA12345678) or 1 letter + 9 digits
        if (residentCert.Length < 10 || residentCert.Length > 12) 
            return false;

        return residentCert.All(c => char.IsLetterOrDigit(c));
    }
}
