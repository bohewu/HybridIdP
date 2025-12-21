using System.Diagnostics;
using System.Net;

namespace Tests.SystemTests;

public class WebIdPServerFixture : IAsyncLifetime
{
    // Static state for shared server
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static Process? _serverProcess;
    private static int _usageCount;
    
    // We use the same port for now to test "Shared Instance" parallelism.
    private const string ServerUrl = "https://localhost:7035";
    public string BaseUrl => ServerUrl;

    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    public async Task InitializeAsync()
    {
        await EnsureServerRunningAsync();
    }
    
    public async Task EnsureServerRunningAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!IsRunning)
            {
                // Cleanup potential stale process from previous runs
                await KillExistingServerAsync();
                await StartServerAsync();
                
                // Register safety net
                AppDomain.CurrentDomain.ProcessExit += (s, e) => 
                {
                    try { _serverProcess?.Kill(entireProcessTree: true); } catch { }
                };
            }
            // Only increment if we are successfully running (or attached)
            Interlocked.Increment(ref _usageCount);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DisposeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            int count = Interlocked.Decrement(ref _usageCount);
            if (count <= 0)
            {
                // Last user left, kill server
                Interlocked.Exchange(ref _usageCount, 0);
                await StopServerAsync();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task StartServerAsync()
    {
        // ... (unchanged arguments)
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --launch-profile https --RateLimiting:Enabled=false --Security:ValidationIntervalSeconds=0",
            WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Web.IdP"),
            UseShellExecute = false,
            CreateNoWindow = false, // Maybe true to avoid popup?
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // ...

        _serverProcess = Process.Start(startInfo);
        if (_serverProcess == null)
            throw new Exception("Failed to start Web.IdP server");
        
        var stderr = new System.Text.StringBuilder();
        _serverProcess.ErrorDataReceived += (sender, args) => {
             if (args.Data != null) stderr.AppendLine(args.Data);
        };
        _serverProcess.BeginErrorReadLine();
        _serverProcess.OutputDataReceived += (sender, args) => { };
        _serverProcess.BeginOutputReadLine();

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 60000) 
        {
            if (_serverProcess.HasExited)
                 throw new Exception($"Web.IdP server process exited prematurely with code {_serverProcess.ExitCode}. Error: {stderr}");

            if (await IsServerAliveAsync()) return;
            await Task.Delay(100);
        }

        try { _serverProcess.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException($"Web.IdP server did not start within 60s. Last error: {stderr}");
    }

    private async Task StopServerAsync()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _serverProcess.WaitForExitAsync(cts.Token); } catch { }
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }
        catch { }
        
        // Only aggressive Kill if still alive
        if (await IsServerAliveAsync())
        {
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
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1) };
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
            // Simple robust kill by name
            foreach (var process in Process.GetProcessesByName("Web.IdP"))
            {
                try { process.Kill(); } catch { }
            }
            
            // Also kill any dotnet process that has Web.IdP in cmdline? Too risky.
            // Just rely on Name check and _serverProcess tracking.
            
            await Task.Delay(200);
        }
        catch { }
    }

}
