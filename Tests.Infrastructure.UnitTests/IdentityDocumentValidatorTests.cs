using Infrastructure.Validators;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for IdentityDocumentValidator (Phase 10.6)
/// </summary>
public class IdentityDocumentValidatorTests
{
    #region Taiwan National ID Tests

    [Theory]
    [InlineData("A123456789")] // Valid - Tested and verified
    [InlineData("B123456780")] // Valid - Tested and verified
    [InlineData("F131104093")] // Valid - Example from documentation
    public void IsValidTaiwanNationalId_WithValidId_ShouldReturnTrue(string nationalId)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidTaiwanNationalId(nationalId);

        // Assert
        Assert.True(result, $"Expected {nationalId} to be valid");
    }

    [Theory]
    [InlineData("A123456780")] // Invalid - wrong checksum
    [InlineData("B123456788")] // Invalid - wrong checksum
    [InlineData("F131104099")] // Invalid - wrong checksum
    public void IsValidTaiwanNationalId_WithInvalidChecksum_ShouldReturnFalse(string nationalId)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidTaiwanNationalId(nationalId);

        // Assert
        Assert.False(result, $"Expected {nationalId} to be invalid due to wrong checksum");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidTaiwanNationalId_WithNullOrEmpty_ShouldReturnFalse(string? nationalId)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidTaiwanNationalId(nationalId);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("A12345678")] // Too short (9 chars instead of 10)
    [InlineData("A1234567890")] // Too long (11 chars instead of 10)
    [InlineData("1123456789")] // Starts with digit instead of letter
    [InlineData("AA23456789")] // Two letters at start
    [InlineData("A12345678A")] // Letter at end instead of digit
    public void IsValidTaiwanNationalId_WithInvalidFormat_ShouldReturnFalse(string nationalId)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidTaiwanNationalId(nationalId);

        // Assert
        Assert.False(result, $"Expected {nationalId} to be invalid due to format");
    }

    [Fact]
    public void IsValidTaiwanNationalId_WithLowercaseLetter_ShouldHandleCorrectly()
    {
        // Arrange - using lowercase version of valid ID
        var nationalId = "a123456789";

        // Act
        var result = IdentityDocumentValidator.IsValidTaiwanNationalId(nationalId);

        // Assert
        Assert.True(result, "Should handle lowercase letters by converting to uppercase");
    }

    #endregion

    #region Passport Number Tests

    [Theory]
    [InlineData("123456")] // 6 characters (minimum)
    [InlineData("300123456")] // 9 characters (Taiwan passport format)
    [InlineData("AB1234567")] // 9 alphanumeric
    [InlineData("ABCDEF123456")] // 12 characters (maximum)
    public void IsValidPassportNumber_WithValidPassport_ShouldReturnTrue(string passportNumber)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidPassportNumber(passportNumber);

        // Assert
        Assert.True(result, $"Expected {passportNumber} to be valid");
    }

    [Theory]
    [InlineData("12345")] // Too short (5 chars)
    [InlineData("ABCDEF1234567")] // Too long (13 chars)
    [InlineData("ABC-123456")] // Contains invalid character (hyphen)
    [InlineData("ABC 123456")] // Contains space
    public void IsValidPassportNumber_WithInvalidPassport_ShouldReturnFalse(string passportNumber)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidPassportNumber(passportNumber);

        // Assert
        Assert.False(result, $"Expected {passportNumber} to be invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidPassportNumber_WithNullOrEmpty_ShouldReturnFalse(string? passportNumber)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidPassportNumber(passportNumber);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Resident Certificate Tests

    [Theory]
    [InlineData("AA12345678")] // 10 characters (2 letters + 8 digits)
    [InlineData("A123456789")] // 10 characters (1 letter + 9 digits)
    [InlineData("AB12345678")] // 10 alphanumeric
    [InlineData("ABC123456789")] // 12 characters (maximum)
    public void IsValidResidentCertificateNumber_WithValidCertificate_ShouldReturnTrue(string residentCert)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidResidentCertificateNumber(residentCert);

        // Assert
        Assert.True(result, $"Expected {residentCert} to be valid");
    }

    [Theory]
    [InlineData("A12345678")] // Too short (9 chars)
    [InlineData("ABCD12345678X")] // Too long (13 chars)
    [InlineData("AA-12345678")] // Contains invalid character (hyphen)
    [InlineData("AA 12345678")] // Contains space
    public void IsValidResidentCertificateNumber_WithInvalidCertificate_ShouldReturnFalse(string residentCert)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidResidentCertificateNumber(residentCert);

        // Assert
        Assert.False(result, $"Expected {residentCert} to be invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidResidentCertificateNumber_WithNullOrEmpty_ShouldReturnFalse(string? residentCert)
    {
        // Act
        var result = IdentityDocumentValidator.IsValidResidentCertificateNumber(residentCert);

        // Assert
        Assert.False(result);
    }

    #endregion
}
