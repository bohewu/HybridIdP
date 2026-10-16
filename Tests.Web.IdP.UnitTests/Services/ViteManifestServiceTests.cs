using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Moq;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public sealed class ViteManifestServiceTests : IDisposable
{
    private readonly string _webRootPath = Path.Combine(
        Path.GetTempPath(), "HybridIdP-ViteManifestTests-" + Guid.NewGuid().ToString("N"));

    public ViteManifestServiceTests()
    {
        Directory.CreateDirectory(_webRootPath);
    }

    [Fact]
    public void GetCssPaths_ShouldIncludeTransitiveStaticImportsOnce_InManifestOrder()
    {
        var service = CreateService("""
            {
              "src/profile/main.js": {
                "file": "assets/profile.js",
                "css": ["assets/profile.css", "assets/profile.css"],
                "imports": ["_recovery.js", "_other.js"],
                "dynamicImports": ["_dynamic.js"]
              },
              "_recovery.js": {
                "file": "assets/recovery.js",
                "css": ["assets/recovery.css"],
                "imports": ["_shared.js"]
              },
              "_other.js": {
                "file": "assets/other.js",
                "css": ["assets/recovery.css", "assets/other.css"],
                "imports": ["_shared.js"]
              },
              "_shared.js": { "file": "assets/shared.js", "css": ["assets/shared.css"] },
              "_dynamic.js": { "file": "assets/dynamic.js", "css": ["assets/dynamic.css"] }
            }
            """);

        Assert.Equal(new[]
        {
            "/dist/assets/profile.css",
            "/dist/assets/recovery.css",
            "/dist/assets/shared.css",
            "/dist/assets/other.css"
        }, service.GetCssPaths("src/profile/main.js"));
    }

    [Fact]
    public void GetCssPaths_ShouldIncludeImportedCss_WhenEntryHasNoOwnCss()
    {
        var service = CreateService("""
            {
              "src/main.js": { "file": "assets/main.js", "imports": ["_shared.js"] },
              "_shared.js": { "file": "assets/shared.js", "css": ["assets/shared.css"] }
            }
            """);

        Assert.Equal(new[] { "/dist/assets/shared.css" }, service.GetCssPaths("src/main.js"));
    }

    [Fact]
    public void GetCssPaths_ShouldTerminateCyclesAndSkipMissingImports()
    {
        var service = CreateService("""
            {
              "src/main.js": {
                "file": "assets/main.js", "css": ["assets/main.css"],
                "imports": ["_shared.js", "_missing.js"]
              },
              "_shared.js": {
                "file": "assets/shared.js", "css": ["assets/shared.css"],
                "imports": ["src/main.js"]
              }
            }
            """);

        Assert.Equal(new[] { "/dist/assets/main.css", "/dist/assets/shared.css" },
            service.GetCssPaths("src/main.js"));
    }

    [Fact]
    public void GetCssPaths_ShouldReturnEmpty_WhenEntryIsMissing()
    {
        var service = CreateService("{}");

        Assert.Empty(service.GetCssPaths("src/missing.js"));
    }

    [Theory]
    [InlineData("src/main.js", null)]
    [InlineData("src/main.css", "http://localhost:5173/src/main.css")]
    public void GetCssPaths_ShouldPreserveDevelopmentBehavior(string entryName, string? expectedCss)
    {
        var service = CreateService("""
            {
              "src/main.js": { "file": "assets/main.js", "css": ["assets/main.css"] }
            }
            """, "Development");

        if (expectedCss == null)
        {
            Assert.Empty(service.GetCssPaths(entryName));
        }
        else
        {
            Assert.Equal(new[] { expectedCss }, service.GetCssPaths(entryName));
        }
    }

    private ViteManifestService CreateService(string manifest, string environmentName = "Production")
    {
        File.WriteAllText(Path.Combine(_webRootPath, "manifest.json"), manifest);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        environment.SetupGet(value => value.WebRootPath).Returns(_webRootPath);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vite:Manifest"] = "manifest.json" })
            .Build();
        return new ViteManifestService(environment.Object, configuration);
    }

    public void Dispose()
    {
        File.Delete(Path.Combine(_webRootPath, "manifest.json"));
        Directory.Delete(_webRootPath);
    }
}
