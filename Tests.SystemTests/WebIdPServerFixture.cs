using System.Diagnostics;
using System.Net;

namespace Tests.SystemTests;

/// <summary>
/// Manages Web.IdP server lifecycle for system tests
/// Single instance shared by SystemTestsCollection
/// </summary>
public class WebIdPServerFixture : IAsyncLifetime, IDisposable
{
    private Process? _serverProcess;
    private const string ServerUrl = "https://localhost:7035";
    public string BaseUrl => ServerUrl;

    public bool IsRunning 
    {
        get { return _serverProcess != null && !_serverProcess.HasExited; }
    }

    public async Task InitializeAsync()
    {
        await EnsureServerRunningAsync();
    }

    public async Task DisposeAsync()
    {
        await StopServerAsync();
    }
    
    // For manual calls if needed (though InitializeAsync covers it)
    public async Task EnsureServerRunningAsync()
    {
        if (IsRunning && await IsServerAliveAsync()) return;

        await KillExistingServerAsync();
        await StartServerAsync();
    }

    public async Task StopServerAsync()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _serverProcess.WaitForExitAsync(cts.Token); } catch { }
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }
        catch { }
        
        await KillExistingServerAsync();
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
            // 1. Kill by process name (Cross-platform .NET API)
            foreach (var process in Process.GetProcessesByName("Web.IdP"))
            {
                try { process.Kill(); } catch { }
            }
            
            // 2. Kill by port 7035 (OS-specific)
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                await KillPortWindowsAsync(7035);
            }
            else
            {
                await KillPortLinuxAsync(7035);
            }

            await Task.Delay(2000); // Wait for release
        }
        catch { }
    }

    private async Task KillPortWindowsAsync(int port)
    {
            var netstatInfo = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c netstat -ano | findstr :{port}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var netstatProcess = Process.Start(netstatInfo);
            if (netstatProcess != null)
            {
                var output = await netstatProcess.StandardOutput.ReadToEndAsync();
                await netstatProcess.WaitForExitAsync();

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // On Windows netstat -ano output, PID is the last column
                    if (parts.Length > 0 && int.TryParse(parts[^1], out int pid))
                    {
                        try
                        {
                            var process = Process.GetProcessById(pid);
                            // Verify it's not the current test runner!
                            if (process.Id != Environment.ProcessId)
                            {
                                process.Kill(entireProcessTree: true);
                                await process.WaitForExitAsync();
                            }
                        }
                        catch { }
                    }
                }
            }
    }

    private async Task KillPortLinuxAsync(int port)
    {
        try 
        {
            // lsof -t -i:7035
            var lsofinfo = new ProcessStartInfo
            {
                FileName = "lsof",
                Arguments = $"-t -i:{port}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(lsofinfo);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                     var pids = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                     foreach(var pidStr in pids)
                     {
                         if (int.TryParse(pidStr, out int pid))
                         {
                             try { Process.GetProcessById(pid).Kill(); } catch { }
                         }
                     }
                }
            }
        }
        catch 
        { 
             try { Process.Start("pkill", "-f Web.IdP")?.WaitForExit(); } catch {}
        }
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
            throw new Exception("Failed to start Web.IdP server");
        
        // Capture stderr for debugging
        var stderr = new System.Text.StringBuilder();
        _serverProcess.ErrorDataReceived += (sender, args) => {
             if (args.Data != null) stderr.AppendLine(args.Data);
        };
        _serverProcess.BeginErrorReadLine();

        // Drain stdout
        _serverProcess.OutputDataReceived += (sender, args) => { };
        _serverProcess.BeginOutputReadLine();

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 60000) 
        {
            if (_serverProcess.HasExited)
                 throw new Exception($"Web.IdP server process exited prematurely with code {_serverProcess.ExitCode}. Error: {stderr}");

            if (await IsServerAliveAsync()) return;
            await Task.Delay(500);
        }

        try { _serverProcess.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException($"Web.IdP server did not start within 60s. Last error: {stderr}");
    }
    
    public void Dispose()
    {
        // IAsyncLifetime.DisposeAsync handles logical cleanup. 
        // This is just fallback.
        GC.SuppressFinalize(this);
    }
}
