using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Tests.Infrastructure.IntegrationTests;

public sealed class ProviderMetadataRefreshIntegrationTests
{
    [Fact]
    public async Task RefreshAsync_CompletedBoundAccount_PersistsProducerMetadataWithoutChangingRecoveryEmailOrBinding()
    {
        var accountId = Guid.NewGuid();
        var directoryObjectId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.Users.Add(new ApplicationUser
        {
            Id = accountId,
            UserName = "bound-account",
            NormalizedUserName = "BOUND-ACCOUNT",
            RecoverySourceBootstrapRevokedAtUtc = DateTimeOffset.Parse("2026-09-01T00:00:00Z")
        });
        var binding = new ProviderSubjectDirectoryBinding(
            accountId,
            "example.provider",
            "opaque-subject",
            directoryObjectId,
            DateTime.Parse("2026-08-01T00:00:00Z").ToUniversalTime());
        context.ProviderSubjectDirectoryBindings.Add(binding);
        var migration = new CredentialMigrationStateRecord(
            accountId,
            binding.Id,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        migration.Advance(
            CredentialMigrationState.ProofValidated,
            EffectiveEmailOtpRequirement.NotRequired,
            DateTimeOffset.Parse("2026-08-01T00:01:00Z"));
        migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, DateTimeOffset.Parse("2026-08-01T00:02:00Z"));
        migration.Advance(CredentialMigrationState.LocalFinalized, DateTimeOffset.Parse("2026-08-01T00:03:00Z"));
        context.CredentialMigrationStateRecords.Add(migration);
        var recoveryEmail = new RecoveryEmailRecord(
            accountId,
            "recovery@example.test",
            "RECOVERY@EXAMPLE.TEST",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        recoveryEmail.MarkVerified(DateTimeOffset.Parse("2026-08-01T00:04:00Z"));
        context.RecoveryEmails.Add(recoveryEmail);
        await context.SaveChangesAsync();

        const string producerJson =
            "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"opaque-subject\"," +
            "\"legacyCategories\":[\"category-a\",\"category-b\"],\"legacyStatus\":\"Complete\"," +
            "\"email\":\"source@example.test\",\"emailTrustOrigin\":\"SourceVerified\"," +
            "\"verifiedAt\":\"2026-09-07T20:00:00+08:00\",\"legacySourceTimestamp\":\"2026-09-07T20:01:00+08:00\"}";
        var handler = new ProducerStubHandler(producerJson);
        var service = new ProviderMetadataRefreshService(
            context,
            new HttpClient(handler),
            Options.Create(new ProviderMetadataRefreshOptions
            {
                Enabled = true,
                Endpoint = "http://provider.example.test:8080/api/authenticate/metadata",
                SharedSecret = "integration-secret",
                AllowPrivateNetworkHttp = true,
                Timeout = TimeSpan.FromSeconds(5)
            }),
            new FixedTimeProvider());

        var outcome = await service.RefreshAsync("example.provider", "opaque-subject");

        Assert.Equal(ProviderMetadataRefreshOutcome.Refreshed, outcome);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/authenticate/metadata", handler.Path);
        Assert.Equal("integration-secret", handler.InternalSecret);
        Assert.NotNull(handler.RequestJson);
        using (var request = JsonDocument.Parse(handler.RequestJson!))
        {
            var properties = request.RootElement.EnumerateObject().ToArray();
            Assert.Equal(3, properties.Length);
            Assert.Equal("1.0", request.RootElement.GetProperty("contractVersion").GetString());
            Assert.Equal("example.provider", request.RootElement.GetProperty("providerNamespace").GetString());
            Assert.Equal("opaque-subject", request.RootElement.GetProperty("stableSubject").GetString());
            Assert.DoesNotContain(properties, property =>
                property.Name.Contains("password", StringComparison.OrdinalIgnoreCase));
        }

        var snapshot = await context.ProviderMetadataSnapshots.SingleAsync();
        Assert.Equal(binding.Id, snapshot.ProviderSubjectDirectoryBindingId);
        Assert.Equal("source@example.test", snapshot.Email);
        Assert.Equal(ProviderEmailTrustOrigin.SourceVerified, snapshot.EmailTrustOrigin);
        Assert.Equal(DateTimeOffset.Parse("2026-09-07T12:00:00Z"), snapshot.VerifiedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T00:00:00Z"), snapshot.RefreshedAtUtc);

        var persistedBinding = await context.ProviderSubjectDirectoryBindings.SingleAsync();
        Assert.Equal(binding.Id, persistedBinding.Id);
        Assert.Equal(accountId, persistedBinding.LocalAccountId);
        Assert.Equal(directoryObjectId, persistedBinding.DirectoryObjectId);
        Assert.Equal("example.provider", persistedBinding.ProviderNamespace);
        Assert.Equal("opaque-subject", persistedBinding.StableSubject);
        Assert.Equal(CredentialMigrationState.LocalFinalized, (await context.CredentialMigrationStateRecords.SingleAsync()).State);
        var persistedRecovery = await context.RecoveryEmails.SingleAsync();
        Assert.Equal("recovery@example.test", persistedRecovery.Address);
        Assert.Equal(DateTimeOffset.Parse("2026-08-01T00:04:00Z"), persistedRecovery.VerifiedAtUtc);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            (await context.Users.AsNoTracking().SingleAsync()).RecoverySourceBootstrapRevokedAtUtc);
    }

    private sealed class ProducerStubHandler(string responseJson) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? InternalSecret { get; private set; }
        public string? RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            InternalSecret = request.Headers.GetValues("X-Internal-Secret").Single();
            RequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-09-08T00:00:00Z");
    }
}
