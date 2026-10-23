using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;

namespace Web.IdP.Startup;

public static class DataProtectionStartup
{
    private const string LockFileName = ".hybrididp-keyring.lock";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    private sealed record SharedKeyDirectoryOptions(DirectoryInfo Directory);

    public static void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keysDirectory = configuration["DataProtection:KeysDirectory"] ?? "/app/keys";
        if (!Directory.Exists(keysDirectory) && environment.IsDevelopment())
        {
            return;
        }

        var directory = new DirectoryInfo(keysDirectory);
        services.AddDataProtection()
            .PersistKeysToFileSystem(directory)
            .SetApplicationName(configuration["Branding:AppName"] ?? "HybridIdP");
        services.AddSingleton(new SharedKeyDirectoryOptions(directory));
    }

    public static async Task<WebApplication> BuildApplicationAsync(
        WebApplicationBuilder builder,
        CancellationToken cancellationToken = default)
    {
        var sharedKeys = builder.Services.LastOrDefault(
            service => service.ServiceType == typeof(SharedKeyDirectoryOptions))
            ?.ImplementationInstance as SharedKeyDirectoryOptions;
        if (sharedKeys is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return builder.Build();
        }

        WebApplication? app = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            sharedKeys.Directory.Create();
            using var keyLock = await AcquireLockAsync(sharedKeys.Directory, cancellationToken);

            // Hold the lock before building: construction of application services can
            // already resolve Data Protection and populate its long-lived key-ring cache.
            app = builder.Build();
            cancellationToken.ThrowIfCancellationRequested();
            var protector = app.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("HybridIdP.Startup.KeyRingCheck.v1");
            const string probe = "key-ring-check";
            if (protector.Unprotect(protector.Protect(probe)) != probe)
            {
                throw new InvalidOperationException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            app.Logger.LogInformation("Shared Data Protection key initialization completed.");
            return app;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (app is not null) await app.DisposeAsync();
            throw;
        }
        catch (Exception)
        {
            if (app is not null) await app.DisposeAsync();
            // Do not include storage paths or key material in the startup error.
            throw new InvalidOperationException("Shared Data Protection startup failed.");
        }
    }

    private static async Task<FileStream> AcquireLockAsync(
        DirectoryInfo directory,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var path = Path.Combine(directory.FullName, LockFileName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Keep the file after releasing the handle. Deleting it can let a
                // contender lock a different inode while another node still owns it.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                var remaining = LockTimeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("Shared Data Protection initialization lock timed out.");
                }

                await Task.Delay(remaining < TimeSpan.FromMilliseconds(100)
                    ? remaining : TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
    }
}
