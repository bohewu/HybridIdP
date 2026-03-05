using System.Linq;
using Microsoft.AspNetCore.RateLimiting;
using Web.IdP.Controllers.Connect;
using Xunit;

namespace Tests.Application.UnitTests;

public class AuthorizationControllerRateLimitingTests
{
    [Fact]
    public void Authorize_ActionHasDedicatedAuthorizeRateLimitPolicy()
    {
        var method = typeof(AuthorizationController).GetMethod(nameof(AuthorizationController.Authorize));

        Assert.NotNull(method);
        var attribute = method!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        Assert.Equal("authorize", attribute.PolicyName);
    }
}
