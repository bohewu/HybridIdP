using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Web.IdP.Startup;

namespace Tests.Web.IdP.UnitTests.Startup;

public sealed class DataProtectionStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hybrididp-startup-" + Guid.NewGuid());
    private string KeysDirectory => Path.Combine(_root, "keys");
    private string LockPath => Path.Combine(KeysDirectory, ".hybrididp-keyring.lock");

    [Fact]
    public async Task BuildApplicationAsync_ShouldShareOneUsableKey_WhenStartingConcurrentlyWithEmptyStore()
    {
        var firstBuilder = CreateBuilder();
        var secondBuilder = CreateBuilder();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<WebApplication> StartAsync(WebApplicationBuilder builder)
        {
            await start.Task;
            return await DataProtectionStartup.BuildApplicationAsync(builder);
        }

        var first = Task.Run(() => StartAsync(firstBuilder));
        var second = Task.Run(() => StartAsync(secondBuilder));
        Assert.False(Directory.Exists(KeysDirectory));
        start.SetResult();
        await using var appA = await first;
        await using var appB = await second;

        Assert.Single(Directory.GetFiles(KeysDirectory, "key-*.xml"));
        var protectorA = GetProtector(appA.Services);
        var protectorB = GetProtector(appB.Services);
        Assert.Equal("from-a", protectorB.Unprotect(protectorA.Protect("from-a")));
        Assert.Equal("from-b", protectorA.Unprotect(protectorB.Protect("from-b")));
        Assert.Equal(
            Assert.Single(appA.Services.GetRequiredService<IKeyManager>().GetAllKeys()).KeyId,
            Assert.Single(appB.Services.GetRequiredService<IKeyManager>().GetAllKeys()).KeyId);
    }

    [Theory]
    [InlineData(60, 1)]
    [InlineData(1, 2)]
    public async Task BuildApplicationAsync_ShouldPreserveOldPayloadAndKeyBytes_AndAllowNormalRotation(
        int daysUntilExpiry, int expectedKeyCount)
    {
        Directory.CreateDirectory(KeysDirectory);
        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(KeysDirectory))
            .SetApplicationName("StartupTests")
            .DisableAutomaticKeyGeneration();
        using var oldProvider = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        oldProvider.GetRequiredService<IKeyManager>().CreateNewKey(now.AddDays(-30), now.AddDays(daysUntilExpiry));
        var oldProtector = GetProtector(oldProvider);
        var oldPayload = oldProtector.Protect("existing-payload");
        var originalPath = Assert.Single(Directory.GetFiles(KeysDirectory, "key-*.xml"));
        var originalBytes = await File.ReadAllBytesAsync(originalPath);

        await using var app = await DataProtectionStartup.BuildApplicationAsync(CreateBuilder());

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Equal("existing-payload", GetProtector(app.Services).Unprotect(oldPayload));
        Assert.Equal("new-payload", oldProtector.Unprotect(GetProtector(app.Services).Protect("new-payload")));
        var keys = app.Services.GetRequiredService<IKeyManager>().GetAllKeys();
        Assert.Equal(expectedKeyCount, keys.Count);
        if (daysUntilExpiry == 1)
        {
            // The framework schedules a successor at the current key's expiry.
            Assert.Contains(keys, key => key.ActivationDate > now && key.ExpirationDate > now.AddDays(80));
        }
    }

    [Fact]
    public async Task BuildApplicationAsync_ShouldWaitBeforeBuilding_WhenAnotherInitializerHoldsLock()
    {
        Directory.CreateDirectory(KeysDirectory);
        using var heldLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var builder = CreateBuilder();
        var initialization = DataProtectionStartup.BuildApplicationAsync(builder);
        Assert.False(initialization.IsCompleted);
        Assert.Empty(Directory.GetFiles(KeysDirectory, "key-*.xml"));

        heldLock.Dispose();
        await using var app = await initialization;
        Assert.Single(Directory.GetFiles(KeysDirectory, "key-*.xml"));
        using var releasedLock = new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task BuildApplicationAsync_ShouldFailWithoutKeys_WhenLockWaitExpires()
    {
        Directory.CreateDirectory(KeysDirectory);
        using var heldLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var elapsed = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DataProtectionStartup.BuildApplicationAsync(CreateBuilder()));

        Assert.Equal("Shared Data Protection startup failed.", failure.Message);
        Assert.Null(failure.InnerException);
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(45));
        Assert.Empty(Directory.GetFiles(KeysDirectory, "key-*.xml"));
    }

    [Fact]
    public async Task BuildApplicationAsync_ShouldCancelWithoutKeys_WhenWaitingForLock()
    {
        Directory.CreateDirectory(KeysDirectory);
        using var heldLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        var initialization = DataProtectionStartup.BuildApplicationAsync(CreateBuilder(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);
        Assert.Empty(Directory.GetFiles(KeysDirectory, "key-*.xml"));
    }

    [Fact]
    public async Task BuildApplicationAsync_ShouldFailWithoutFallback_WhenKeyDirectoryIsUnavailable()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(KeysDirectory, "not-a-directory");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DataProtectionStartup.BuildApplicationAsync(CreateBuilder()));

        Assert.Equal("Shared Data Protection startup failed.", failure.Message);
        Assert.Equal("not-a-directory", await File.ReadAllTextAsync(KeysDirectory));
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task BuildApplicationAsync_ShouldDisposeApplicationAndReleaseLock_WhenProviderCannotInitialize()
    {
        var builder = CreateBuilder();
        builder.Services.AddDataProtection().DisableAutomaticKeyGeneration();
        var lifetime = new DisposalProbe();
        builder.Services.AddSingleton(_ => lifetime);
        builder.Services.AddSingleton<IDataProtectionProvider>(provider =>
        {
            provider.GetRequiredService<DisposalProbe>();
            throw new InvalidOperationException("private-provider-failure");
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DataProtectionStartup.BuildApplicationAsync(builder));

        Assert.Equal("Shared Data Protection startup failed.", failure.Message);
        Assert.True(lifetime.Disposed);
        Assert.Empty(Directory.GetFiles(KeysDirectory, "key-*.xml"));
        using var releasedLock = new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task BuildApplicationAsync_ShouldKeepFrameworkDefaults_WhenDevelopmentDirectoryIsAbsent()
    {
        await using var app = await DataProtectionStartup.BuildApplicationAsync(CreateBuilder(Environments.Development));

        Assert.False(Directory.Exists(KeysDirectory));
    }

    private WebApplicationBuilder CreateBuilder(string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(DataProtectionStartup).Assembly.GetName().Name
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:KeysDirectory"] = KeysDirectory,
            ["Branding:AppName"] = "StartupTests"
        });
        builder.Logging.ClearProviders();
        DataProtectionStartup.ConfigureServices(builder.Services, builder.Configuration, builder.Environment);
        return builder;
    }

    private static IDataProtector GetProtector(IServiceProvider services) =>
        services.GetRequiredService<IDataProtectionProvider>().CreateProtector("existing-cookie-purpose");

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
