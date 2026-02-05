using Core.Application.Utilities;
using Xunit;

namespace Tests.Application.UnitTests;

public class NameFormatterTests
{
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