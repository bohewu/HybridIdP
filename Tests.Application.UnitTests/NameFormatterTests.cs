using Core.Application.Utilities;
using Xunit;

namespace Tests.Application.UnitTests;

public class NameFormatterTests
{
    [Theory]
    [InlineData(null, null, null, " \u6e2c\u8a66\u59d3\u540d ", "\u6e2c\u8a66\u59d3\u540d")]
    [InlineData(" Given ", "Middle", "Family", "Source full name", "Given Middle Family")]
    [InlineData(" ", null, "\t", "", null)]
    [InlineData(null, null, null, " \t ", null)]
    public void BuildDisplayName_ShouldUseNativeNicknameOnlyWhenStructuredNameIsMissing(
        string? first, string? middle, string? last, string nickname, string? expected)
    {
        Assert.Equal(expected, NameFormatter.BuildDisplayName(first, middle, last, nickname));
    }

    [Fact]
    public void BuildDisplayName_WithFirstNameOnly_ReturnsFirstName()
    {
        var result = NameFormatter.BuildDisplayName("Wei", null, null);
        Assert.Equal("Wei", result);
    }

    [Fact]
    public void BuildDisplayName_WithFirstAndLastName_JoinsWithSpace()
    {
        var result = NameFormatter.BuildDisplayName("Wei", null, "Chen");
        Assert.Equal("Wei Chen", result);
    }

    [Fact]
    public void BuildDisplayName_WithFirstMiddleLastName_JoinsAllParts()
    {
        var result = NameFormatter.BuildDisplayName("Wei", "M.", "Chen");
        Assert.Equal("Wei M. Chen", result);
    }

    [Fact]
    public void BuildDisplayName_WithWhitespaceParts_TrimmedAndSkipped()
    {
        var result = NameFormatter.BuildDisplayName("  Wei ", " ", "  Chen  ");
        Assert.Equal("Wei Chen", result);
    }

    [Fact]
    public void BuildDisplayName_WithAllNull_ReturnsNull()
    {
        var result = NameFormatter.BuildDisplayName(null, null, null);
        Assert.Null(result);
    }
}
