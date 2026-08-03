using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tests.SystemTests;

public class WebIdPServerFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static Process? _serverProcess;
    private static EphemeralHttpsCertificate? _serverCertificate;
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

    public async Task<bool> VerifyUserSecurityStampRemainsUnchangedAsync(
        Guid userId,
        Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var originalSecurityStamp = await ReadUserSecurityStampAsync(userId);
        if (originalSecurityStamp is null)
        {
            return false;
        }

        await operation();

        var currentSecurityStamp = await ReadUserSecurityStampAsync(userId);
        return string.Equals(
            originalSecurityStamp,
            currentSecurityStamp,
            StringComparison.Ordinal);
    }

    private async Task StartServerAsync(
        bool enablePrivilegedTestAdminBootstrap,
        bool disableClientWriteEndpoints)
    {
        EphemeralHttpsCertificate? certificate = null;
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
        try
        {
            certificate = EphemeralHttpsCertificate.Create();
            startInfo.EnvironmentVariables["Kestrel__Certificates__Default__Path"] = certificate.Path;
            startInfo.EnvironmentVariables["Kestrel__Certificates__Default__Password"] = certificate.Password;

            _serverProcess = Process.Start(startInfo);
            if (_serverProcess == null)
            {
                throw new InvalidOperationException(
                    "Failed to start the Web.IdP test server.");
            }

            _serverCertificate = certificate;
            certificate = null;
            _serverCertificate.ClearPassword();

            _serverProcess.ErrorDataReceived += (_, _) => { };
            _serverProcess.BeginErrorReadLine();
            _serverProcess.OutputDataReceived += (_, _) => { };
            _serverProcess.BeginOutputReadLine();

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 60000)
            {
                if (_serverProcess.HasExited)
                {
                    var exitCode = _serverProcess.ExitCode;
                    await StopServerAsync();
                    throw new InvalidOperationException(
                        $"Web.IdP test server exited prematurely with code {exitCode} " +
                        $"while using database provider '{databaseProvider}'.");
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
                $"while using database provider '{databaseProvider}'.");
        }
        catch
        {
            if (_serverProcess != null)
            {
                await StopServerAsync();
            }

            certificate?.Dispose();
            throw;
        }
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
            DisposeServerCertificate();
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

    private static async Task<string?> ReadUserSecurityStampAsync(Guid userId)
    {
        var webIdpDirectory = GetWebIdPDirectory();
        var databaseProvider = ResolveDatabaseProvider();
        var (_, connectionString) = ResolveTestConnectionString(webIdpDirectory, databaseProvider);
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (databaseProvider == PostgreSqlProvider)
        {
            optionsBuilder.UseNpgsql(
                connectionString,
                options => options.MigrationsAssembly("Infrastructure.Migrations.Postgres"));
        }
        else
        {
            optionsBuilder.UseSqlServer(
                connectionString,
                options => options.MigrationsAssembly("Infrastructure.Migrations.SqlServer"));
        }

        optionsBuilder.UseOpenIddict<Guid>();

        await using var dbContext = new ApplicationDbContext(optionsBuilder.Options);
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleOrDefaultAsync();
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
                finally
                {
                    DisposeServerCertificate();
                }
            };
            _processExitHandlerRegistered = true;
        }
    }

    private static void DisposeServerCertificate()
    {
        var certificate = _serverCertificate;
        _serverCertificate = null;
        certificate?.Dispose();
    }

    private sealed class EphemeralHttpsCertificate : IDisposable
    {
        private readonly string _directory;
        private string? _password;

        private EphemeralHttpsCertificate(string directory, string path, string password)
        {
            _directory = directory;
            Path = path;
            _password = password;
        }

        public string Path { get; }

        public string Password => _password ?? throw new InvalidOperationException(
            "The ephemeral HTTPS certificate password is no longer available.");

        public static EphemeralHttpsCertificate Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "HybridAuthIdP-SystemTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var path = System.IO.Path.Combine(directory, "https.pfx");
                using var key = RSA.Create(2048);
                var request = new CertificateRequest(
                    "CN=localhost",
                    key,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                        true));
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                        true));
                var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
                subjectAlternativeNames.AddDnsName("localhost");
                subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
                subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
                request.CertificateExtensions.Add(subjectAlternativeNames.Build());
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                using var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddHours(2));
                var pfx = certificate.Export(X509ContentType.Pfx, password);
                try
                {
                    File.WriteAllBytes(path, pfx);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pfx);
                }

                return new EphemeralHttpsCertificate(directory, path, password);
            }
            catch
            {
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public void ClearPassword()
        {
            _password = null;
        }

        public void Dispose()
        {
            _password = null;
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
