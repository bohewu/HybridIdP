using Core.Application.Options;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class DataSeederTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly SettingsService _settingsService;

    public DataSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _dataProtectionProvider = new EphemeralDataProtectionProvider();
        _settingsService = new SettingsService(
            _context,
            _cache,
            _dataProtectionProvider);
    }

    [Fact]
    public async Task SeedDefaultSettingsAsync_ProtectsConfiguredSmtpPassword()
    {
        const string configuredPassword = "configured-smtp-password";
        var configuration = CreateEmailConfiguration(configuredPassword);

        await DataSeeder.SeedDefaultSettingsAsync(
            _context,
            configuration,
            _settingsService);

        var storedPassword = await _context.Settings
            .Where(setting => setting.Key == SettingKeys.Email.SmtpPassword)
            .Select(setting => setting.Value)
            .SingleAsync();

        Assert.NotNull(storedPassword);
        Assert.NotEqual(configuredPassword, storedPassword);
        Assert.Equal(
            configuredPassword,
            _dataProtectionProvider
                .CreateProtector("SettingsService")
                .Unprotect(storedPassword));
        Assert.Equal(
            "smtp.example.test",
            await _context.Settings
                .Where(setting => setting.Key == SettingKeys.Email.SmtpHost)
                .Select(setting => setting.Value)
                .SingleAsync());
    }

    [Fact]
    public async Task SeedDefaultSettingsAsync_ReprotectsMatchingLegacyPlaintextPassword()
    {
        const string configuredPassword = "legacy-plaintext-password";
        _context.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            Key = SettingKeys.Email.SmtpPassword,
            Value = configuredPassword
        });
        await _context.SaveChangesAsync();

        await DataSeeder.SeedDefaultSettingsAsync(
            _context,
            CreateEmailConfiguration(configuredPassword),
            _settingsService);

        var storedPassword = await _context.Settings
            .Where(setting => setting.Key == SettingKeys.Email.SmtpPassword)
            .Select(setting => setting.Value)
            .SingleAsync();

        Assert.NotNull(storedPassword);
        Assert.NotEqual(configuredPassword, storedPassword);
        Assert.Equal(
            configuredPassword,
            _dataProtectionProvider
                .CreateProtector("SettingsService")
                .Unprotect(storedPassword));
    }

    [Fact]
    public async Task SeedDefaultSettingsAsync_DoesNotReplaceExistingDatabasePasswordOverride()
    {
        const string databasePassword = "database-smtp-password";
        await _settingsService.SetValueAsync(
            SettingKeys.Email.SmtpPassword,
            databasePassword,
            "TestUser");

        await DataSeeder.SeedDefaultSettingsAsync(
            _context,
            CreateEmailConfiguration("different-config-password"),
            _settingsService);

        Assert.Equal(
            databasePassword,
            await _settingsService.GetValueAsync(SettingKeys.Email.SmtpPassword));
    }

    [Fact]
    public async Task SeedDefaultSettingsAsync_PreservesEmptyPasswordForUnauthenticatedSmtp()
    {
        await DataSeeder.SeedDefaultSettingsAsync(
            _context,
            CreateEmailConfiguration(string.Empty),
            _settingsService);

        Assert.Equal(
            string.Empty,
            await _context.Settings
                .Where(setting => setting.Key == SettingKeys.Email.SmtpPassword)
                .Select(setting => setting.Value)
                .SingleAsync());
    }

    private static IConfiguration CreateEmailConfiguration(string smtpPassword)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:SmtpHost"] = "smtp.example.test",
                [$"{EmailOptions.SectionName}:SmtpPort"] = "587",
                [$"{EmailOptions.SectionName}:SmtpEnableSsl"] = "true",
                [$"{EmailOptions.SectionName}:SmtpUsername"] = "smtp-user",
                [$"{EmailOptions.SectionName}:SmtpPassword"] = smtpPassword,
                [$"{EmailOptions.SectionName}:FromAddress"] = "sender@example.test",
                [$"{EmailOptions.SectionName}:FromName"] = "Test Sender"
            })
            .Build();
    }

    public void Dispose()
    {
        _context.Dispose();
        _cache.Dispose();
    }
}
