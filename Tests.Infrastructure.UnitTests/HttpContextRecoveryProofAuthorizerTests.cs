using System.Security.Claims;
using Core.Domain.Constants;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class HttpContextRecoveryProofAuthorizerTests
{
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
