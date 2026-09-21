using Core.Application.DTOs;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class CredentialMigrationOptionsTests
{
    [Fact]
    public void Defaults_AreDisabledAndUseProtectedTransport()
    {
        var directory = new DirectoryIntegrationOptions();
        var migration = new CredentialMigrationOptions();

        Assert.False(directory.Enabled);
        Assert.False(directory.AuthenticationEnabled);
        Assert.False(migration.Enabled);
        Assert.Equal(DirectoryTransport.Ldaps, directory.Transport);
        Assert.Equal(EmailOtpPolicy.Disabled, migration.EmailOtpPolicyFloor);
    }

    [Fact]
    public void Validators_AcceptStageOneAndStageTwoProfiles()
    {
        var stageOneDirectory = new DirectoryIntegrationOptions { Enabled = true };
        var stageTwoDirectory = new DirectoryIntegrationOptions
        {
            Enabled = true,
            AuthenticationEnabled = true,
            Transport = DirectoryTransport.StartTls
        };

        var directoryValidator = new DirectoryIntegrationOptionsValidator();
        var stageTwoMigrationValidator = new CredentialMigrationOptionsValidator(
            Options.Create(stageTwoDirectory),
            Options.Create(new LegacyPasswordSyncOptions()));

        Assert.True(directoryValidator.Validate(null, stageOneDirectory).Succeeded);
        Assert.True(directoryValidator.Validate(null, stageTwoDirectory).Succeeded);
        Assert.True(stageTwoMigrationValidator.Validate(null, new CredentialMigrationOptions { Enabled = true }).Succeeded);
    }

    [Theory]
    [InlineData(DirectoryTransport.Anonymous)]
    [InlineData(DirectoryTransport.SimpleBind)]
    public void DirectoryValidator_RejectsUnprotectedTransport(DirectoryTransport transport)
    {
        var result = new DirectoryIntegrationOptionsValidator().Validate(null, new DirectoryIntegrationOptions
        {
            Enabled = true,
            Transport = transport
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void CredentialMigrationValidator_RejectsInvalidSwitchCombinations()
    {
        var directoryValidator = new DirectoryIntegrationOptionsValidator();
        var directoryAuthenticationWithoutIntegration = new DirectoryIntegrationOptions { AuthenticationEnabled = true };
        var migrationWithoutDirectoryAuthentication = new CredentialMigrationOptionsValidator(
            Options.Create(new DirectoryIntegrationOptions { Enabled = true }),
            Options.Create(new LegacyPasswordSyncOptions()));
        var migrationWithLegacyOnly = new CredentialMigrationOptionsValidator(
            Options.Create(new DirectoryIntegrationOptions { Enabled = true }),
            Options.Create(new LegacyPasswordSyncOptions
            {
                Enabled = true,
                Stage2MigrationEnabled = true
            }));

        Assert.False(directoryValidator.Validate(null, directoryAuthenticationWithoutIntegration).Succeeded);
        Assert.False(migrationWithoutDirectoryAuthentication
            .Validate(null, new CredentialMigrationOptions { Enabled = true }).Succeeded);
        Assert.True(migrationWithLegacyOnly
            .Validate(null, new CredentialMigrationOptions { Enabled = true }).Succeeded);
    }

    [Fact]
    public void EmailOtpPolicy_UsesTheStricterRequestedOrDeploymentFloor()
    {
        var options = new CredentialMigrationOptions { EmailOtpPolicyFloor = EmailOtpPolicy.ProviderRequested };

        Assert.Equal(EmailOtpPolicy.ProviderRequested, options.GetEffectiveEmailOtpPolicy(EmailOtpPolicy.Disabled));
        Assert.Equal(EmailOtpPolicy.ProviderRequested, options.GetEffectiveEmailOtpPolicy(EmailOtpPolicy.ProviderRequested));
        Assert.Equal(EmailOtpPolicy.Required, options.GetEffectiveEmailOtpPolicy(EmailOtpPolicy.Required));
    }

    [Fact]
    public void LegacyProofPolicy_RespectsWindowCutoffAndSunset()
    {
        var now = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var options = new CredentialMigrationOptions
        {
            Enabled = true,
            MigrationWindowStartsAtUtc = now.AddMinutes(-1),
            MigrationWindowEndsAtUtc = now.AddMinutes(10),
            SunsetAtUtc = now.AddMinutes(5)
        };

        Assert.True(options.IsLegacyProofAllowed(now));
        Assert.False(options.IsLegacyProofAllowed(now.AddMinutes(6)));
        Assert.False(new CredentialMigrationOptions { Enabled = true, OperatorCutoff = true }.IsLegacyProofAllowed(now));
        Assert.False(new CredentialMigrationOptions().IsLegacyProofAllowed(now));
    }
}
