using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class M2MSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;

    public M2MSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task ClientCredentialsMock_EndToEnd_ReturnsSuccess()
    {
        // Arrange
        var projectDir = GetProjectDirectory();
        var testClientDir = Path.Combine(projectDir, "..", "TestClient.M2M");
        var outputPath = Path.Combine(Path.GetTempPath(), $"m2m_results_{Guid.NewGuid()}.json");

        // Act
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{testClientDir}\" -- --output \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var errors = new List<string>();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) output.Add(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errors.Add(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        // Debug output if failed
        if (process.ExitCode != 0)
        {
            var outputData = string.Join(Environment.NewLine, output);
            var errorData = string.Join(Environment.NewLine, errors);
            Assert.Fail($"Process failed with exit code {process.ExitCode}.\nOutput: {outputData}\nError: {errorData}");
        }

        // Assert
        Assert.True(File.Exists(outputPath), "Result file was not created.");
        var json = await File.ReadAllTextAsync(outputPath);
        
        try
        {
            var result = JsonSerializer.Deserialize<TestResult>(json);
            Assert.NotNull(result);
            Assert.True(result.Success, $"Test failed: {result.Message}");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private string GetProjectDirectory()
    {
        // This is a simple helper to find the project root from bin/Debug/...
        var current = Directory.GetCurrentDirectory();
        // Assuming we are running from bin/Debug/netX.X/
        // We want to go up to Tests.SystemTests
        // But simpler: just traverse up until we find the solution folder or assume relative path
        // Since we are running `dotnet test` from solution root often, or project root...
        // Let's assume standard build output structure.
        
        // Actually, just using relative paths from execution context might be flaky.
        // A better way is to find the .sln file and go from there.
        
        var dir = new DirectoryInfo(current);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HybridAuthIdP.sln")))
        {
            dir = dir.Parent;
        }
        
        if (dir == null) throw new Exception("Could not find solution root.");
        return Path.Combine(dir.FullName, "Tests.SystemTests"); // Assuming project folder name
    }
}

public class TestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
