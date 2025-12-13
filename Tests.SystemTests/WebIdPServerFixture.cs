using System.Diagnostics;
using System.Net;

namespace Tests.SystemTests;

/// <summary>
/// Manages Web.IdP server lifecycle for system tests
/// </summary>
public class WebIdPServerFixture : IDisposable
{
    private Process? _serverProcess;
    private const string ServerUrl = "https://localhost:7035";
    private const int StartupTimeoutMs = 30000;
    
    public string BaseUrl => ServerUrl;
    public bool IsRunning { get; private set; }

    public async Task EnsureServerRunningAsync()
    {
        // Check if already running
        if (await IsServerAliveAsync())
        {
            IsRunning = true;
            return;
        }

        // Kill any existing process using the port
        await KillExistingServerAsync();

        // Start new server process
        await StartServerAsync();
    }

    private async Task<bool> IsServerAliveAsync()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"{ServerUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task KillExistingServerAsync()
    {
        try
        {
            // Find process using port 7035
            var netstatInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano | findstr :7035",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var netstatProcess = Process.Start(netstatInfo);
            if (netstatProcess != null)
            {
                var output = await netstatProcess.StandardOutput.ReadToEndAsync();
                await netstatProcess.WaitForExitAsync();

                // Parse PID from output (last column)
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[^1], out int pid))
                    {
                        try
                        {
                            var process = Process.GetProcessById(pid);
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync();
                        }
                        catch { /* Process already gone */ }
                    }
                }
            }

            // Additional wait for port to be released
            await Task.Delay(2000);
        }
        catch { /* Continue if kill fails */ }
    }

    private async Task StartServerAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --launch-profile https --no-build",
            WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Web.IdP"),
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _serverProcess = Process.Start(startInfo);
        if (_serverProcess == null)
        {
            throw new Exception("Failed to start Web.IdP server");
        }

        // Wait for server to be ready
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < StartupTimeoutMs)
        {
            if (await IsServerAliveAsync())
            {
                IsRunning = true;
                await Task.Delay(1000); // Additional stabilization time
                return;
            }
            await Task.Delay(500);
        }

        throw new TimeoutException($"Web.IdP server did not start within {StartupTimeoutMs}ms");
    }

    public void Dispose()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                _serverProcess.WaitForExit(5000);
                _serverProcess.Dispose();
            }
        }
        catch { /* Ignore disposal errors */ }
        
        IsRunning = false;
    }
}
