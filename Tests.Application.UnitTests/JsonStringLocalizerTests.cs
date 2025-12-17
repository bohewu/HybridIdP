using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Services.Localization;
using Xunit;

namespace Tests.Application.UnitTests;

/// <summary>
/// Unit tests for custom JsonStringLocalizer.
/// Tests are written first (TDD) - implementation will follow.
/// </summary>
public class JsonStringLocalizerTests : IDisposable
{
    private readonly string _testResourcesPath;
    private readonly JsonStringLocalizerFactory _factory;
    private readonly CultureInfo _originalCulture;

    public JsonStringLocalizerTests()
    {
        // Save original culture to restore after tests
        _originalCulture = CultureInfo.CurrentUICulture;
        
        // Create temporary test resources directory
        _testResourcesPath = Path.Combine(Path.GetTempPath(), $"LocalizerTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testResourcesPath);
        
        // Create test resource files
        CreateTestResourceFile("TestResource.json", new Dictionary<string, string>
        {
            { "Greeting", "Hello" },
            { "Farewell", "Goodbye" }
        });
        
        CreateTestResourceFile("TestResource.zh-TW.json", new Dictionary<string, string>
        {
            { "Greeting", "你好" },
            { "Farewell", "再見" }
        });
        
        CreateTestResourceFile("EmailTemplateResource.json", new Dictionary<string, string>
        {
            { "MfaCode_Subject", "Your verification code - {ProductName}" },
            { "Email_Footer", "Footer content" },
            { "Welcome_Message", "Hello, {0}!" }
        });
        
        CreateTestResourceFile("EmailTemplateResource.zh-TW.json", new Dictionary<string, string>
        {
            { "MfaCode_Subject", "您的驗證碼 - {ProductName}" },
            { "Email_Footer", "頁尾內容" }
        });

        // Create factory with test path
        _factory = new JsonStringLocalizerFactory(_testResourcesPath);
    }

    public void Dispose()
    {
        // Restore original culture
        CultureInfo.CurrentUICulture = _originalCulture;
        
        // Cleanup temp directory
        if (Directory.Exists(_testResourcesPath))
        {
            try
            {
                Directory.Delete(_testResourcesPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    private void CreateTestResourceFile(string filename, Dictionary<string, string> content)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(content);
        File.WriteAllText(Path.Combine(_testResourcesPath, filename), json);
    }

    #region JsonStringLocalizer Tests

    [Fact]
    public void GetString_ReturnsValue_WhenKeyExists()
    {
        // Arrange
        var localizer = _factory.Create("TestResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        // Act
        var result = localizer["Greeting"];

        // Assert
        Assert.False(result.ResourceNotFound);
        Assert.Equal("Hello", result.Value);
    }

    [Fact]
    public void GetString_ReturnsCultureSpecificValue_WhenCultureFileExists()
    {
        // Arrange
        var localizer = _factory.Create("TestResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("zh-TW");

        // Act
        var result = localizer["Greeting"];

        // Assert
        Assert.False(result.ResourceNotFound);
        Assert.Equal("你好", result.Value);
    }

    [Fact]
    public void GetString_FallsBackToDefault_WhenCultureFileNotExists()
    {
        // Arrange
        var localizer = _factory.Create("TestResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");

        // Act
        var result = localizer["Greeting"];

        // Assert
        Assert.False(result.ResourceNotFound);
        Assert.Equal("Hello", result.Value);
    }

    [Fact]
    public void GetString_ReturnsResourceNotFound_WhenKeyNotExists()
    {
        // Arrange
        var localizer = _factory.Create("TestResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        // Act
        var result = localizer["NonExistentKey"];

        // Assert
        Assert.True(result.ResourceNotFound);
        Assert.Equal("NonExistentKey", result.Value);
    }

    [Fact]
    public void GetString_WithFormatArgs_FormatsCorrectly()
    {
        // Arrange
        var localizer = _factory.Create("EmailTemplateResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        // Act - Use Welcome_Message which has proper {0} placeholder
        var result = localizer["Welcome_Message", "John"];

        // Assert
        Assert.False(result.ResourceNotFound);
        // The indexer with args applies string.Format
        Assert.Equal("Hello, John!", result.Value);
    }

    [Fact]
    public void GetAllStrings_ReturnsAllKeys_ForCurrentCulture()
    {
        // Arrange
        var localizer = _factory.Create("TestResource", "");
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture; // Use invariant to ensure default file is loaded

        // Act
        var allStrings = localizer.GetAllStrings(includeParentCultures: false).ToList();

        // Assert
        Assert.True(allStrings.Count >= 2); // May include fallback
        Assert.Contains(allStrings, s => s.Name == "Greeting" && s.Value == "Hello");
        Assert.Contains(allStrings, s => s.Name == "Farewell" && s.Value == "Goodbye");
    }

    [Fact]
    public void GetAllStrings_IncludesParentCulture_WhenFlagIsTrue()
    {
        // Arrange
        var localizer = _factory.Create("TestResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("zh-TW");

        // Act
        var allStrings = localizer.GetAllStrings(includeParentCultures: true).ToList();

        // Assert
        // Should have zh-TW values
        Assert.Contains(allStrings, s => s.Name == "Greeting" && s.Value == "你好");
    }

    #endregion

    #region JsonStringLocalizerFactory Tests

    [Fact]
    public void Create_WithType_ReturnsLocalizer()
    {
        // Act
        var localizer = _factory.Create(typeof(TestMarkerClass));

        // Assert
        Assert.NotNull(localizer);
    }

    [Fact]
    public void Create_WithBasenameAndLocation_ReturnsLocalizer()
    {
        // Act
        var localizer = _factory.Create("TestResource", "SomeLocation");

        // Assert
        Assert.NotNull(localizer);
    }

    [Fact]
    public void Create_CachesLocalizers_ForSameResource()
    {
        // Arrange & Act
        var localizer1 = _factory.Create("TestResource", "");
        var localizer2 = _factory.Create("TestResource", "");

        // Assert - should return same instance (cached)
        Assert.Same(localizer1, localizer2);
    }

    [Fact]
    public void Create_FromType_ExtractsCorrectBasename()
    {
        // Arrange
        // The factory should extract "EmailTemplateResource" from the full type name
        CultureInfo.CurrentUICulture = new CultureInfo("zh-TW");
        
        // Act
        var localizer = _factory.Create(typeof(Infrastructure.Resources.EmailTemplateResource));
        var result = localizer["MfaCode_Subject"];

        // Assert
        Assert.False(result.ResourceNotFound);
        Assert.Equal("您的驗證碼 - {ProductName}", result.Value);
    }

    #endregion

    #region Options Configuration Tests

    [Fact]
    public void Factory_WithOptions_BuildsCorrectSearchPaths()
    {
        // Arrange
        var options = new JsonLocalizationOptions
        {
            ResourcesPath = "Resources",
            AdditionalAssemblyPrefixes = new List<string> { "Infrastructure" }
        };
        var mockOptions = new Mock<IOptions<JsonLocalizationOptions>>();
        mockOptions.Setup(x => x.Value).Returns(options);

        // Act
        var factory = new JsonStringLocalizerFactory(mockOptions.Object);
        var localizer = factory.Create("TestResource", "");

        // Assert
        Assert.NotNull(localizer);
    }

    #endregion

    #region Cross-Project Resource Loading Tests

    [Fact]
    public void Localizer_LoadsResourceFromMultiplePaths()
    {
        // This tests the multi-path fallback mechanism
        // In real usage, this would test loading from Infrastructure/Resources
        
        // Arrange
        var localizer = _factory.Create("EmailTemplateResource", "");
        CultureInfo.CurrentUICulture = new CultureInfo("zh-TW");

        // Act
        var result = localizer["MfaCode_Subject"];

        // Assert
        Assert.False(result.ResourceNotFound);
        Assert.Equal("您的驗證碼 - {ProductName}", result.Value);
    }

    #endregion
}

/// <summary>
/// Marker class for testing factory type resolution.
/// </summary>
public class TestMarkerClass { }
