using System.Security.Claims;
using Core.Domain.Constants;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class HttpContextRecoveryProofAuthorizerTests
{
    [Theory]
    [InlineData("Identity.TwoFactorUserId")]
    [InlineData("Bearer")]
    public async Task EnabledSelection_RejectsNonApplicationIdentityEvenWithMfa(string authenticationType)
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)], authenticationType));
        var authorizer = new HttpContextRecoveryProofAuthorizer(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            Mock.Of<IAuthorizationService>(), Options.Create(new RecoveryEmailSelectionOptions { Enabled = true }));
        Assert.False(await authorizer.IsSelfServiceAuthorizedAsync(id));
    }

    [Fact]
    public async Task DisabledSelection_PreservesLegacyBearerAuthorization()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)], "Bearer"));
        Assert.True(await CreateAuthorizer(principal).IsSelfServiceAuthorizedAsync(id));
    }

    [Theory]
    [InlineData("required-password-change.pending")]
    [InlineData("credential-migration.continuation")]
    [InlineData("credential-migration.recovery-continuation")]
    public async Task IncompleteContinuation_RejectsEvenFullMfaPrincipal(string key)
    {
        var id = Guid.NewGuid();
        var bytes = System.Text.Encoding.UTF8.GetBytes("pending");
        var session = new Mock<ISession>();
        session.Setup(s => s.TryGetValue(key, out bytes)).Returns(true);
        var http = new DefaultHttpContext { User = CreatePrincipal(id, AuthConstants.Amr.Mfa), Session = session.Object };
        var authorizer = new HttpContextRecoveryProofAuthorizer(new HttpContextAccessor { HttpContext = http }, Mock.Of<IAuthorizationService>());
        Assert.False(await authorizer.IsSelfServiceAuthorizedAsync(id));
    }

    [Fact]
    public async Task IsSelfServiceAuthorizedAsync_PasswordOnlyPrincipal_DeniesRecoveryEmailMutations()
    {
        var accountId = Guid.NewGuid();
        var authorizer = CreateAuthorizer(CreatePrincipal(accountId, AuthConstants.Amr.Password));

        var authorized = await authorizer.IsSelfServiceAuthorizedAsync(accountId);

        Assert.False(authorized);
    }

    [Theory]
    [InlineData(AuthConstants.Amr.Mfa)]
    [InlineData(AuthConstants.Amr.HardwareKey)]
    public async Task IsSelfServiceAuthorizedAsync_AssuredCorrectSubject_AllowsRecoveryEmailActions(
        string authenticationMethod)
    {
        var accountId = Guid.NewGuid();
        var authorizer = CreateAuthorizer(CreatePrincipal(accountId, authenticationMethod));

        var authorized = await authorizer.IsSelfServiceAuthorizedAsync(accountId);

        Assert.True(authorized);
    }

    [Fact]
    public async Task IsSelfServiceAuthorizedAsync_AssuredWrongSubject_ReturnsFalse()
    {
        var authorizer = CreateAuthorizer(CreatePrincipal(Guid.NewGuid(), AuthConstants.Amr.Mfa));

        var authorized = await authorizer.IsSelfServiceAuthorizedAsync(Guid.NewGuid());

        Assert.False(authorized);
    }

    [Fact]
    public async Task IsSelfServiceAuthorizedAsync_UnauthenticatedPrincipal_ReturnsFalse()
    {
        var accountId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, accountId.ToString()),
                new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa)
            ]);
        var authorizer = CreateAuthorizer(new ClaimsPrincipal(identity));

        var authorized = await authorizer.IsSelfServiceAuthorizedAsync(accountId);

        Assert.False(authorized);
    }

    private static HttpContextRecoveryProofAuthorizer CreateAuthorizer(ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { User = principal };
        return new HttpContextRecoveryProofAuthorizer(
            new HttpContextAccessor { HttpContext = context },
            Mock.Of<IAuthorizationService>());
    }

    private static ClaimsPrincipal CreatePrincipal(Guid accountId, string authenticationMethod) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, accountId.ToString()),
                new Claim(AuthConstants.ClaimTypes.Amr, authenticationMethod)
            ],
            "Identity.Application"));
}
