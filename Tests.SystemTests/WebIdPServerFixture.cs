using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Tests.SystemTests;

public class WebIdPServerFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static Process? _serverProcess;
    private static int _usageCount;
    private static bool _processExitHandlerRegistered;
    
    private const string ServerUrl = "https://localhost:7035";
    private const string SqlServerProvider = "SqlServer";
    private const string PostgreSqlProvider = "PostgreSQL";
    private const string TestDatabaseProviderVariable = "TEST_DATABASE_PROVIDER";
    private const string TestSqlServerConnectionStringVariable = "TEST_SQLSERVER_CONNECTION_STRING";
    private const string TestPostgreSqlConnectionStringVariable = "TEST_POSTGRESQL_CONNECTION_STRING";
    public string BaseUrl => ServerUrl;

    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

    public async Task InitializeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureServerRunningUnderLockAsync();
            checked
            {
                _usageCount++;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
    
    public async Task EnsureServerRunningAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureServerRunningUnderLockAsync();
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
            if (_usageCount <= 0)
            {
                throw new InvalidOperationException(
                    "The shared Web.IdP fixture was disposed without a matching initialization.");
            }

            _usageCount--;
            if (_usageCount == 0)
            {
                await StopServerAsync();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RunIsolatedClientAdminHostAsync(
        bool enablePrivilegedTestAdminBootstrap,
        bool disableClientWriteEndpoints,
        Func<Task> test)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!IsRunning)
            {
                throw new InvalidOperationException(
                    "The shared Web.IdP server must be running before an isolated host is started.");
            }

            if (_usageCount != 1)
            {
                throw new InvalidOperationException(
                    "An isolated Web.IdP host requires a non-parallel test collection with no other active server fixtures.");
            }

            Exception? isolatedFailure = null;
            try
            {
                await StopServerAsync();
                await StartServerAsync(
                    enablePrivilegedTestAdminBootstrap,
                    disableClientWriteEndpoints);
                await test();
            }
            catch (Exception exception)
            {
                isolatedFailure = exception;
            }

            Exception? restoreFailure = null;
            try
            {
                await StopServerAsync();
                await StartServerAsync(
                    enablePrivilegedTestAdminBootstrap: true,
                    disableClientWriteEndpoints: false);
            }
            catch (Exception exception)
            {
                restoreFailure = exception;
            }

            if (isolatedFailure != null && restoreFailure != null)
            {
                throw new AggregateException(
                    "The isolated client-admin host failed and the shared host could not be restored.",
                    isolatedFailure,
                    restoreFailure);
            }

            if (restoreFailure != null)
            {
                throw new InvalidOperationException(
                    "The shared Web.IdP host could not be restored after isolated execution.",
                    restoreFailure);
            }

            if (isolatedFailure != null)
            {
                throw isolatedFailure;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task StartServerAsync(
        bool enablePrivilegedTestAdminBootstrap,
        bool disableClientWriteEndpoints)
    {
        var webIdpDirectory = GetWebIdPDirectory();
        var buildConfiguration = GetBuildConfiguration();
        var databaseProvider = ResolveDatabaseProvider();
        var (connectionStringName, connectionString) =
            ResolveTestConnectionString(webIdpDirectory, databaseProvider);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = string.Join(
                " ",
                "run",
                "--no-build",
                "--no-restore",
                "--configuration",
                buildConfiguration,
                "--launch-profile https",
                "--RateLimiting:Enabled=false",
                "--Security:ValidationIntervalSeconds=0",
                $"--SeedData:PrivilegedTestAdminBootstrap:Enabled={enablePrivilegedTestAdminBootstrap.ToString().ToLowerInvariant()}",
                $"--ClientAdminApiHardening:DisableClientWriteEndpoints={disableClientWriteEndpoints.ToString().ToLowerInvariant()}"),
            WorkingDirectory = webIdpDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.EnvironmentVariables["DATABASE_PROVIDER"] = databaseProvider;
        startInfo.EnvironmentVariables[$"ConnectionStrings__{connectionStringName}"] = connectionString;
        startInfo.EnvironmentVariables["OpenIddict__UseEphemeralKeysForTesting"] = "true";

        _serverProcess = Process.Start(startInfo);
        if (_serverProcess == null)
        {
            throw new InvalidOperationException("Failed to start the Web.IdP test server.");
        }
        
        var stderr = new System.Text.StringBuilder();
        _serverProcess.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                stderr.AppendLine(args.Data);
            }
        };
        _serverProcess.BeginErrorReadLine();
        _serverProcess.OutputDataReceived += (_, _) => { };
        _serverProcess.BeginOutputReadLine();

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 60000)
        {
            if (_serverProcess.HasExited)
            {
                var exitCode = _serverProcess.ExitCode;
                _serverProcess.Dispose();
                _serverProcess = null;
                throw new InvalidOperationException(
                    $"Web.IdP test server exited prematurely with code {exitCode} " +
                    $"while using database provider '{databaseProvider}'. Error: {stderr}");
            }

            if (await IsServerAliveAsync())
            {
                return;
            }

            await Task.Delay(100);
        }

        await StopServerAsync();
        throw new TimeoutException(
            $"Web.IdP test server did not start within 60 seconds " +
            $"while using database provider '{databaseProvider}'. Last error: {stderr}");
    }

    private async Task StopServerAsync()
    {
        var process = _serverProcess;
        _serverProcess = null;

        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await process.WaitForExitAsync(cts.Token); } catch { }
            }
        }
        catch { }
        finally
        {
            process?.Dispose();
        }
        
        if (await IsServerAliveAsync())
        {
            throw new InvalidOperationException(
                $"The Web.IdP listener at {ServerUrl} remained active after the tracked test process stopped. " +
                "The fixture will not terminate an untracked process.");
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

    private static string GetWebIdPDirectory()
    {
        var directory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Web.IdP"));

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"The Web.IdP project directory was not found at '{directory}'.");
        }

        return directory;
    }

    private static string GetBuildConfiguration()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var buildConfiguration = outputDirectory.Parent?.Name;
        if (string.IsNullOrWhiteSpace(buildConfiguration))
        {
            throw new InvalidOperationException(
                $"The test build configuration could not be resolved from '{AppContext.BaseDirectory}'.");
        }

        return buildConfiguration;
    }

    private static string ResolveDatabaseProvider()
    {
        var configuredProvider =
            Environment.GetEnvironmentVariable(TestDatabaseProviderVariable) ?? SqlServerProvider;

        if (configuredProvider.Equals(SqlServerProvider, StringComparison.OrdinalIgnoreCase))
        {
            return SqlServerProvider;
        }

        if (configuredProvider.Equals(PostgreSqlProvider, StringComparison.OrdinalIgnoreCase) ||
            configuredProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            return PostgreSqlProvider;
        }

        throw new InvalidOperationException(
            $"{TestDatabaseProviderVariable} must be '{SqlServerProvider}' or '{PostgreSqlProvider}'.");
    }

    private static (string Name, string Value) ResolveTestConnectionString(
        string webIdpDirectory,
        string databaseProvider)
    {
        var isPostgreSql = databaseProvider == PostgreSqlProvider;
        var connectionStringName =
            isPostgreSql ? "PostgreSqlConnection" : "SqlServerConnection";
        var overrideVariable =
            isPostgreSql
                ? TestPostgreSqlConnectionStringVariable
                : TestSqlServerConnectionStringVariable;
        var overrideValue = Environment.GetEnvironmentVariable(overrideVariable);

        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return (connectionStringName, overrideValue);
        }

        var settingsPath = Path.Combine(webIdpDirectory, "appsettings.Development.json");
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) ||
            !connectionStrings.TryGetProperty(connectionStringName, out var configuredValue) ||
            string.IsNullOrWhiteSpace(configuredValue.GetString()))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{connectionStringName} is required in appsettings.Development.json " +
                $"when {overrideVariable} is not set.");
        }

        return (connectionStringName, configuredValue.GetString()!);
    }

    private async Task EnsureServerRunningUnderLockAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (await IsServerAliveAsync())
        {
            throw new InvalidOperationException(
                $"A Web.IdP listener is already active at {ServerUrl}, but it was not started by this test run. " +
                "Stop the external host before running system tests.");
        }

        await StartServerAsync(
            enablePrivilegedTestAdminBootstrap: true,
            disableClientWriteEndpoints: false);

        if (!_processExitHandlerRegistered)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    _serverProcess?.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            };
            _processExitHandlerRegistered = true;
        }
    }
}
