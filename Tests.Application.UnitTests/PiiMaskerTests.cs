using Core.Application.Options;
using Core.Application.Utilities;
using Xunit;

namespace Tests.Application.UnitTests;

/// <summary>
/// Unit tests for PiiMasker to verify correct PII masking at all levels.
/// </summary>
public class PiiMaskerTests
{
    #region MaskEmail Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskEmail_NullOrEmpty_ReturnsOriginal(string? email)
    {
        // Act
        var result = PiiMasker.MaskEmail(email, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal(email, result);
    }

    [Fact]
    public void MaskEmail_WithNone_ReturnsOriginal()
    {
        // Arrange
        var email = "john.doe@example.com";

        // Act
        var result = PiiMasker.MaskEmail(email, PiiMaskingLevel.None);

        // Assert
        Assert.Equal(email, result);
    }

    [Fact]
    public void MaskEmail_WithStrict_ReturnsFullMask()
    {
        // Arrange
        var email = "john.doe@example.com";

        // Act
        var result = PiiMasker.MaskEmail(email, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal("***", result);
        Assert.DoesNotContain("@", result);
        Assert.DoesNotContain("john", result);
    }

    [Theory]
    [InlineData("john.doe@example.com", "j***@example.com")]
    [InlineData("a@example.com", "a***@example.com")]
    [InlineData("user123@company.org", "u***@company.org")]
    public void MaskEmail_WithPartial_MasksLocalPartPreservesDomain(string email, string expected)
    {
        // Act
        var result = PiiMasker.MaskEmail(email, PiiMaskingLevel.Partial);

        // Assert
        Assert.Equal(expected, result);
        Assert.Contains("@", result); // Domain preserved
        Assert.NotEqual(email, result); // Not the original
    }

    #endregion

    #region MaskName Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskName_NullOrEmpty_ReturnsOriginal(string? name)
    {
        // Act
        var result = PiiMasker.MaskName(name, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal(name, result);
    }

    [Fact]
    public void MaskName_WithNone_ReturnsOriginal()
    {
        // Arrange
        var name = "John Doe";

        // Act
        var result = PiiMasker.MaskName(name, PiiMaskingLevel.None);

        // Assert
        Assert.Equal(name, result);
    }

    [Fact]
    public void MaskName_WithStrict_ReturnsFullMask()
    {
        // Arrange
        var name = "John Doe";

        // Act
        var result = PiiMasker.MaskName(name, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal("***", result);
        Assert.DoesNotContain("John", result);
        Assert.DoesNotContain("Doe", result);
    }

    [Theory]
    [InlineData("王大明", "王*明")] // Chinese name
    [InlineData("John", "J*n")]     // Short Western name
    [InlineData("Li", "L*")]        // 2-char name
    [InlineData("A", "A")]          // Single char - unchanged
    public void MaskName_WithPartial_MasksMiddleChars(string name, string expected)
    {
        // Act
        var result = PiiMasker.MaskName(name, PiiMaskingLevel.Partial);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region MaskUserName Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskUserName_NullOrEmpty_ReturnsOriginal(string? username)
    {
        // Act
        var result = PiiMasker.MaskUserName(username, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal(username, result);
    }

    [Fact]
    public void MaskUserName_WithNone_ReturnsOriginal()
    {
        // Arrange
        var username = "admin_user";

        // Act
        var result = PiiMasker.MaskUserName(username, PiiMaskingLevel.None);

        // Assert
        Assert.Equal(username, result);
    }

    [Fact]
    public void MaskUserName_WithStrict_ReturnsFullMask()
    {
        // Arrange
        var username = "admin_user";

        // Act
        var result = PiiMasker.MaskUserName(username, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal("***", result);
        Assert.DoesNotContain("admin", result);
    }

    [Theory]
    [InlineData("admin_user", "adm***")]   // Shows first 3 chars
    [InlineData("jo", "j*")]               // Short username
    [InlineData("user", "us***")]          // 4 chars
    public void MaskUserName_WithPartial_ShowsFirstChars(string username, string expected)
    {
        // Act
        var result = PiiMasker.MaskUserName(username, PiiMaskingLevel.Partial);

        // Assert
        Assert.Equal(expected, result);
        Assert.NotEqual(username, result);
    }

    #endregion

    #region MaskGeneric Tests

    [Fact]
    public void MaskGeneric_WithStrict_ReturnsFullMask()
    {
        // Arrange
        var value = "sensitive_data_123";

        // Act
        var result = PiiMasker.MaskGeneric(value, PiiMaskingLevel.Strict);

        // Assert
        Assert.Equal("***", result);
    }

    [Theory]
    [InlineData("12345678", "1***8")]    // Shows first and last
    [InlineData("AB", "***")]            // Too short for partial
    public void MaskGeneric_WithPartial_ShowsEnds(string value, string expected)
    {
        // Act
        var result = PiiMasker.MaskGeneric(value, PiiMaskingLevel.Partial);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Security Assertions - Verify No PII Leakage

    [Fact]
    public void AllMaskMethods_WithStrict_NeverLeakOriginalContent()
    {
        // Arrange
        var sensitiveEmail = "secret.user@company.com";
        var sensitiveName = "張三豐";
        var sensitiveUsername = "superadmin";
        var sensitiveValue = "SSN-123-45-6789";

        // Act
        var maskedEmail = PiiMasker.MaskEmail(sensitiveEmail, PiiMaskingLevel.Strict);
        var maskedName = PiiMasker.MaskName(sensitiveName, PiiMaskingLevel.Strict);
        var maskedUsername = PiiMasker.MaskUserName(sensitiveUsername, PiiMaskingLevel.Strict);
        var maskedGeneric = PiiMasker.MaskGeneric(sensitiveValue, PiiMaskingLevel.Strict);

        // Assert - Strict should never contain any part of original
        Assert.DoesNotContain("secret", maskedEmail);
        Assert.DoesNotContain("@", maskedEmail);
        Assert.DoesNotContain("張", maskedName);
        Assert.DoesNotContain("豐", maskedName);
        Assert.DoesNotContain("super", maskedUsername);
        Assert.DoesNotContain("SSN", maskedGeneric);
        Assert.DoesNotContain("123", maskedGeneric);

        // All should be the same mask
        Assert.Equal("***", maskedEmail);
        Assert.Equal("***", maskedName);
        Assert.Equal("***", maskedUsername);
        Assert.Equal("***", maskedGeneric);
    }

    [Fact]
    public void MaskEmail_WithPartial_DoesNotLeakFullLocalPart()
    {
        // Arrange
        var email = "john.doe.secret@example.com";

        // Act
        var result = PiiMasker.MaskEmail(email, PiiMaskingLevel.Partial);

        // Assert
        Assert.DoesNotContain("john.doe.secret", result);
        Assert.DoesNotContain("doe", result);
        Assert.StartsWith("j***@", result); // Only first char visible
    }

    #endregion
}
