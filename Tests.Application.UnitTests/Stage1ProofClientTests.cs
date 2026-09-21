using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests.Application.UnitTests;

public sealed class Stage1ProofClientTests
{
    [Fact]
    public async Task ProveAsync_ValidTypedResponse_ReturnsAuthenticatedContract()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"contractVersion\":\"1.0\",\"outcome\":\"Authenticated\",\"providerNamespace\":\"example.provider\",\"stableSubject\":\"opaque-subject\",\"canonicalAccount\":\"account\",\"assurance\":{\"stableSubjectAssured\":true,\"canonicalAccountAssured\":true}}",
                Encoding.UTF8,
                "application/json")
        });
        var provider = CreateProvider(handler, TimeSpan.FromSeconds(1));

        var result = await provider.ProveAsync(
            new ProofRequest { AccountName = "account" },
            "password");

        Assert.Equal(ProofOutcome.Authenticated, result.Outcome);
        Assert.NotNull(handler.Request);
        Assert.Equal("http://provider.example.org/api/authenticate/login", handler.Request!.RequestUri!.ToString());
        Assert.Equal("secret", handler.Request.Headers.GetValues("X-Internal-Secret").Single());
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("account", payload.RootElement.GetProperty("accountName").GetString());
        Assert.Equal("1.0", payload.RootElement.GetProperty("contractVersion").GetString());
    }

    [Fact]
    public async Task ProveAsync_CallerCancels_PropagatesCancellation()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CreateProvider(handler, TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();

        var proving = provider.ProveAsync(
            new ProofRequest { AccountName = "account" },
            "password",
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proving);
    }

    [Fact]
    public async Task ProveAsync_ProviderExceedsBoundedTimeout_ReturnsTimeout()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = CreateProvider(handler, TimeSpan.FromMilliseconds(25));

        var result = await provider.ProveAsync(
            new ProofRequest { AccountName = "account" },
            "password");

        Assert.Equal(ProofOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task AuthenticateAsync_Stage1ProofDenial_DoesNotFallBackToLegacy()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.FindByEmailAsync("account"))
            .ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(manager => manager.FindByNameAsync("account"))
            .ReturnsAsync((ApplicationUser?)null);
        var legacy = new Mock<ILegacyAuthService>();
        var proof = new Mock<IProofProvider>();
        proof.Setup(provider => provider.ProveAsync(
                It.IsAny<ProofRequest>(),
                "password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProofResult { Outcome = ProofOutcome.InvalidCredentials });

        var login = new LoginService(
            userManager.Object,
            new Mock<ISecurityPolicyService>().Object,
            legacy.Object,
            new Mock<IJitProvisioningService>().Object,
            CreateEmptyDbContext(),
            new Mock<ILogger<LoginService>>().Object,
            Options.Create(new Core.Application.Options.ExternalLoginOptions()),
            proofProvider: proof.Object,
            directoryIdentityLookup: new Mock<IDirectoryIdentityLookup>().Object,
            stage1BindingRefreshService: new Mock<IStage1BindingRefreshService>().Object,
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true }));

        var result = await login.AuthenticateAsync("account", "password");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        legacy.Verify(service => service.ValidateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_Stage1AmbiguousDirectoryLookup_RemainsSuccessfulWithoutBinding()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.FindByEmailAsync("account"))
            .ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(manager => manager.FindByNameAsync("account"))
            .ReturnsAsync((ApplicationUser?)null);
        var proof = new Mock<IProofProvider>();
        proof.Setup(provider => provider.ProveAsync(
                It.IsAny<ProofRequest>(),
                "password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticatedProof());
        var directory = new Mock<IDirectoryIdentityLookup>();
        directory.Setup(lookup => lookup.FindManagedIdentityAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryLookupResult(DirectoryLookupOutcome.Ambiguous));
        var binding = new Mock<IStage1BindingRefreshService>();
        var provisioned = new ApplicationUser { Id = Guid.NewGuid(), UserName = "local", IsActive = true };

        var login = new LoginService(
            userManager.Object,
            new Mock<ISecurityPolicyService>().Object,
            new Mock<ILegacyAuthService>().Object,
            Mock.Of<IJitProvisioningService>(service =>
                service.ProvisionExternalUserAsync(It.IsAny<ExternalAuthResult>(), It.IsAny<CancellationToken>()) ==
                Task.FromResult(provisioned)),
            CreateEmptyDbContext(),
            new Mock<ILogger<LoginService>>().Object,
            Options.Create(new Core.Application.Options.ExternalLoginOptions()),
            proofProvider: proof.Object,
            directoryIdentityLookup: directory.Object,
            stage1BindingRefreshService: binding.Object,
            directoryIntegrationOptions: Options.Create(new DirectoryIntegrationOptions { Enabled = true }));

        var result = await login.AuthenticateAsync("account", "password");

        Assert.Equal(LoginStatus.LegacySuccess, result.Status);
        binding.Verify(service => service.BindAndRefreshAsync(
            It.IsAny<Stage1BindingRefreshRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProviderProofProvider CreateProvider(RecordingHandler handler, TimeSpan timeout) =>
        new(
            new HttpClient(handler),
            Options.Create(new ProviderProofOptions
            {
                Endpoint = "http://provider.example.org/api/authenticate/login",
                SharedSecret = "secret",
                AllowPrivateNetworkHttp = true,
                Timeout = timeout
            }));

    private static IApplicationDbContext CreateEmptyDbContext() =>
        new Infrastructure.ApplicationDbContext(new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
            .UseInMemoryDatabase($"stage1-proof-{Guid.NewGuid():N}")
            .Options);

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<ApplicationUser>>().Object,
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);
    }

    private static ProofResult AuthenticatedProof() => new()
    {
        Outcome = ProofOutcome.Authenticated,
        ProviderNamespace = "example.provider",
        StableSubject = "opaque-subject",
        CanonicalAccount = "account",
        Assurance = new ProofAssurance
        {
            StableSubjectAssured = true,
            CanonicalAccountAssured = true
        }
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this((request, _) => Task.FromResult(response(request)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        {
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _response(request, cancellationToken);
        }
    }
}
