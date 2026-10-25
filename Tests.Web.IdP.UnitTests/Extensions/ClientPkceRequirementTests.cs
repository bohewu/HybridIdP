using Infrastructure.Seeding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tests.Web.IdP.UnitTests.Extensions;

public sealed class ClientPkceRequirementTests
{
    [Theory]
    [InlineData("testclient-public")]
    [InlineData("demo-client-1")]
    public async Task SeededPublicCodeClient_ShouldRejectMissingChallenge(string clientId)
    {
        var applications = new List<OpenIddictApplicationDescriptor>();
        var applicationManager = new Mock<IOpenIddictApplicationManager>();
        applicationManager
            .Setup(manager => manager.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((descriptor, _) => applications.Add(descriptor))
            .ReturnsAsync(new object());
        var scopeManager = new Mock<IOpenIddictScopeManager>();
        scopeManager
            .Setup(manager => manager.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object());

        await ClientSeeder.SeedAsync(applicationManager.Object, scopeManager.Object, seedTestClients: true);

        var application = Assert.Single(applications, descriptor => descriptor.ClientId == clientId);
        Assert.Equal(ClientTypes.Public, application.ClientType);
        Assert.Contains(Permissions.GrantTypes.AuthorizationCode, application.Permissions);
        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, application.Requirements);

        var context = await ValidateRequirementAsync(application, challenge: null);
        Assert.True(context.IsRejected);
        Assert.Equal(Errors.InvalidRequest, context.Error);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task ConfidentialClientRequirement_ShouldRejectOnlyMissingRequiredChallenge(
        bool requirePkce, bool hasChallenge, bool shouldReject)
    {
        var application = new OpenIddictApplicationDescriptor
        {
            ClientId = "synthetic-confidential",
            ClientType = ClientTypes.Confidential
        };
        if (requirePkce)
        {
            application.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        var context = await ValidateRequirementAsync(application, hasChallenge ? new string('A', 43) : null);
        Assert.Equal(shouldReject, context.IsRejected);
        if (shouldReject)
        {
            Assert.Equal(Errors.InvalidRequest, context.Error);
        }
    }

    private static async Task<ValidateAuthorizationRequestContext> ValidateRequirementAsync(
        OpenIddictApplicationDescriptor application, string? challenge)
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager
            .Setup(value => value.FindByClientIdAsync(application.ClientId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        manager
            .Setup(value => value.HasRequirementAsync(application, Requirements.Features.ProofKeyForCodeExchange,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(application.Requirements.Contains(Requirements.Features.ProofKeyForCodeExchange));

        var transaction = new OpenIddictServerTransaction
        {
            Logger = NullLogger.Instance,
            Options = new OpenIddictServerOptions(),
            Request = new OpenIddictRequest
            {
                ClientId = application.ClientId,
                ResponseType = ResponseTypes.Code,
                CodeChallenge = challenge,
                CodeChallengeMethod = challenge is null ? null : CodeChallengeMethods.Sha256
            }
        };
        var context = new ValidateAuthorizationRequestContext(transaction);
        var handler = new OpenIddictServerHandlers.Authentication.ValidateProofKeyForCodeExchangeRequirement(manager.Object);
        await handler.HandleAsync(context);
        return context;
    }
}
