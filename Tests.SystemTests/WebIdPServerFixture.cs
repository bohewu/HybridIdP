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

    public async Task StopServerAsync()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                
                // Wait up to 5 seconds for process to exit
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _serverProcess.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Process didn't exit in time, but Kill was called so it should be gone
                }
                
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }
        catch { /* Ignore stop errors */ }
        
        IsRunning = false;
        
        // Verify server is actually stopped
        await Task.Delay(1000);
        if (await IsServerAliveAsync())
        {
            // Force kill using port
            await KillExistingServerAsync();
        }
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
            // Kill any process named 'Web.IdP' or 'Web.IdP.exe'
            foreach (var process in Process.GetProcessesByName("Web.IdP"))
            {
                try { process.Kill(); } catch { }
            }
            
            // Find process using port 7035 via netstat (backup)
            var netstatInfo = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = "/c netstat -ano | findstr :7035",
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
            Arguments = "run --launch-profile https --no-build --RateLimiting:Enabled=false",
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
        
        // Capture stderr for debugging
        var stderr = new System.Text.StringBuilder();
        _serverProcess.ErrorDataReceived += (sender, args) => {
             if (args.Data != null) stderr.AppendLine(args.Data);
        };
        _serverProcess.BeginErrorReadLine();

        // Drain stdout to prevent buffer fill deadlocks
        _serverProcess.OutputDataReceived += (sender, args) => { /* Ignore stdout */ };
        _serverProcess.BeginOutputReadLine();

        // Wait for server to be ready
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 60000) // 60s timeout
        {
            if (_serverProcess.HasExited)
            {
                 throw new Exception($"Web.IdP server process exited prematurely with code {_serverProcess.ExitCode}. Error: {stderr}");
            }

            if (await IsServerAliveAsync())
            {
                IsRunning = true;
                await Task.Delay(1000); // Additional stabilization time
                return;
            }
            await Task.Delay(500);
        }

        try { _serverProcess.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException($"Web.IdP server did not start within 60s. Last error: {stderr}");
    }

    public void Dispose()
    {
        // Synchronous dispose - best effort cleanup
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
        GC.SuppressFinalize(this);
    }
}
