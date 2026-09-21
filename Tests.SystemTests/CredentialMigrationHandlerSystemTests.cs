using System.Net;
using System.Text.RegularExpressions;
using Core.Application.Ports;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Web.IdP.Middleware;

namespace Tests.SystemTests;

public sealed class CredentialMigrationHandlerSystemTests
{
    [Fact]
    public async Task CredentialMigration_ValidBeginOtpAndCommit_CallsSelectedServices()
    {
        await using var factory = await CredentialMigrationHostFactory.CreateAsync();

        var token = await GetAntiforgeryTokenAsync(factory.Client);
        using var beginResponse = await PostAsync(
            factory.Client,
            "Begin",
            token,
            ("Input.AccountName", "account"),
            ("Input.CurrentPassword", "current-password"));
        var beginHtml = await beginResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        Assert.Equal(1, factory.Ceremony.BeginCalls);
        Assert.Equal(1, factory.Otp.SendCalls);

        token = ExtractAntiforgeryToken(beginHtml);
        using var verifyResponse = await PostAsync(
            factory.Client,
            "VerifyOtp",
            token,
            ("Proof.Code", "123456"));
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.Equal(1, factory.Otp.VerifyCalls);

        token = ExtractAntiforgeryToken(verifyHtml);
        using var commitResponse = await PostAsync(
            factory.Client,
            "Commit",
            token,
            ("Commit.NewPassword", "new-password"),
            ("Commit.ConfirmPassword", "new-password"));

        Assert.Equal(HttpStatusCode.Redirect, commitResponse.StatusCode);
        Assert.Equal(1, factory.Ceremony.CommitCalls);
        Assert.Equal("opaque-proof", factory.Ceremony.LastCommitRequest?.MigrationOtpProof);
    }

    [Fact]
    public async Task CredentialMigration_BeginMissingCurrentPassword_PreservesRequiredErrorAndSkipsService()
    {
        await using var factory = await CredentialMigrationHostFactory.CreateAsync();

        var token = await GetAntiforgeryTokenAsync(factory.Client);
        using var response = await PostAsync(
            factory.Client,
            "Begin",
            token,
            ("Input.AccountName", "account"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.Ceremony.BeginCalls);
        AssertHasValidationError(html, "Input.CurrentPassword");
    }

    [Fact]
    public async Task CredentialMigration_CommitMismatchedConfirmation_PreservesCompareErrorAndSkipsService()
    {
        await using var factory = await CredentialMigrationHostFactory.CreateAsync();

        var token = await GetAntiforgeryTokenAsync(factory.Client);
        using var beginResponse = await PostAsync(
            factory.Client,
            "Begin",
            token,
            ("Input.AccountName", "account"),
            ("Input.CurrentPassword", "current-password"));
        var beginHtml = await beginResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);

        token = ExtractAntiforgeryToken(beginHtml);
        using var verifyResponse = await PostAsync(
            factory.Client,
            "VerifyOtp",
            token,
            ("Proof.Code", "123456"));
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        token = ExtractAntiforgeryToken(verifyHtml);
        using var commitResponse = await PostAsync(
            factory.Client,
            "Commit",
            token,
            ("Commit.NewPassword", "new-password"),
            ("Commit.ConfirmPassword", "different-password"));
        var commitHtml = await commitResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        Assert.Equal(0, factory.Ceremony.CommitCalls);
        AssertHasValidationError(commitHtml, "Commit.ConfirmPassword");
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/Account/CredentialMigration");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ExtractAntiforgeryToken(html);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string handler,
        string antiforgeryToken,
        params (string Name, string Value)[] fields)
    {
        var values = fields
            .Select(field => new KeyValuePair<string, string>(field.Name, field.Value))
            .Append(new KeyValuePair<string, string>("__RequestVerificationToken", antiforgeryToken));
        using var content = new FormUrlEncodedContent(values);
        return await client.PostAsync($"/Account/CredentialMigration?handler={handler}", content);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The credential-migration page must render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void AssertHasValidationError(string html, string fieldName)
    {
        var match = Regex.Match(
            html,
            $"<span[^>]*data-valmsg-for=\"{Regex.Escape(fieldName)}\"[^>]*>(.*?)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.True(match.Success, $"The page must render a validation span for {fieldName}.");
        Assert.False(string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(match.Groups[1].Value)));
    }

    private sealed class CredentialMigrationHostFactory : WebApplicationFactory<SecurityHeadersMiddleware>
    {
        private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
        private readonly string _databaseName = $"credential-migration-handler-{Guid.NewGuid():N}";

        private CredentialMigrationHostFactory()
        {
        }

        public HttpClient Client { get; private set; } = null!;
        public FakeCeremony Ceremony { get; } = new();
        public FakeMigrationOtp Otp { get; } = new();

        public static async Task<CredentialMigrationHostFactory> CreateAsync()
        {
            var factory = new CredentialMigrationHostFactory();
            await EnvironmentLock.WaitAsync();
            const string providerVariable = "DATABASE_PROVIDER";
            const string connectionVariable = "ConnectionStrings__SqlServerConnection";
            var previousProvider = Environment.GetEnvironmentVariable(providerVariable);
            var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);
            try
            {
                Environment.SetEnvironmentVariable(providerVariable, "SqlServer");
                Environment.SetEnvironmentVariable(connectionVariable, "Server=(local);Database=unused");
                factory.Client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                    BaseAddress = new Uri("https://localhost")
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable(providerVariable, previousProvider);
                Environment.SetEnvironmentVariable(connectionVariable, previousConnection);
                EnvironmentLock.Release();
            }

            try
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseProvider"] = "SqlServer",
                    ["ConnectionStrings:SqlServerConnection"] = "Server=(local);Database=unused",
                    ["Redis:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "false",
                    ["Turnstile:Enabled"] = "false",
                    ["SeedData:PrivilegedTestAdminBootstrap:Enabled"] = "false"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<ApplicationDbContext>(
                    options => options.UseInMemoryDatabase(_databaseName));

                services.RemoveAll<IStage2CredentialMigrationService>();
                services.AddSingleton<IStage2CredentialMigrationService>(Ceremony);
                services.RemoveAll<IMigrationOtpProofService>();
                services.AddSingleton<IMigrationOtpProofService>(Otp);
                services.RemoveAll<ICredentialMigrationRecoveryService>();
                services.RemoveAll<IRecoveryEmailService>();
                services.RemoveAll<IRecoveryAssistanceService>();
            });
        }

        public override async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await base.DisposeAsync();
        }
    }

    private sealed class FakeCeremony : IStage2CredentialMigrationService
    {
        public int BeginCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public MigrationCommitCeremonyRequest? LastCommitRequest { get; private set; }

        public Task<MigrationProofCeremonyResult> BeginAsync(
            MigrationProofCeremonyRequest request,
            CancellationToken cancellationToken = default)
        {
            BeginCalls++;
            return Task.FromResult(new MigrationProofCeremonyResult(
                MigrationCeremonyOutcome.ContinuationIssued,
                true,
                "opaque-continuation"));
        }

        public Task<MigrationCommitCeremonyResult> CommitAsync(
            MigrationCommitCeremonyRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            LastCommitRequest = request;
            return Task.FromResult(new MigrationCommitCeremonyResult(MigrationCeremonyOutcome.Completed));
        }

        public Task<DirectoryCredentialResult> AuthenticateCompletedAsync(
            Guid localAccountId,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectoryCredentialResult(DirectoryCredentialOutcome.InvalidCredentials));
    }

    private sealed class FakeMigrationOtp : IMigrationOtpProofService
    {
        public int SendCalls { get; private set; }
        public int VerifyCalls { get; private set; }

        public Task<MigrationOtpSendResult> SendAsync(
            MigrationOtpSendRequest request,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            return Task.FromResult(new MigrationOtpSendResult(RecoveryProofOutcome.Success));
        }

        public Task<MigrationOtpVerificationResult> VerifyAsync(
            MigrationOtpVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            return Task.FromResult(new MigrationOtpVerificationResult(
                RecoveryProofOutcome.Success,
                "opaque-proof"));
        }

        public Task<RecoveryProofOutcome> ConsumeAsync(
            MigrationOtpConsumptionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RecoveryProofOutcome.Success);
    }
}
