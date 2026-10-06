using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenIddict.Validation.AspNetCore;
using System.Security.Claims;
using Web.IdP.Attributes;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Attributes;

public class ValidateCsrfForCookiesAttributeTests
{
    private readonly Mock<IAntiforgery> _antiforgeryMock;

    public ValidateCsrfForCookiesAttributeTests()
    {
        _antiforgeryMock = new Mock<IAntiforgery>();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task SafeMethods_ShouldNotValidateCsrf(string method)
    {
        // Arrange
        var context = CreateContext(method, isAuthenticated: false);
        var attribute = new ValidateCsrfForCookiesAttribute();
        var executed = false;

        // Act
        await attribute.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Assert
        Assert.True(executed);
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    [Fact]
    public async Task BearerAuth_ShouldSkipCsrfValidation()
    {
        // Arrange
        var context = CreateContext("POST", isAuthenticated: true, authScheme: "Bearer");
        var attribute = new ValidateCsrfForCookiesAttribute();
        var executed = false;

        // Act
        await attribute.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Assert
        Assert.True(executed);
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    [Fact]
    public async Task JwtAuth_ShouldSkipCsrfValidation()
    {
        // Arrange
        var context = CreateContext("POST", isAuthenticated: true, authScheme: "JwtBearer");
        var attribute = new ValidateCsrfForCookiesAttribute();
        var executed = false;

        // Act
        await attribute.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Assert
        Assert.True(executed);
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    [Fact]
    public async Task CookieAuth_WithValidToken_ShouldAllowRequest()
    {
        // Arrange
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .Returns(Task.CompletedTask);

        var context = CreateContext("POST", isAuthenticated: true, authScheme: "Identity.Application");
        var attribute = new ValidateCsrfForCookiesAttribute();
        var executed = false;

        // Act
        await attribute.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Assert
        Assert.True(executed);
        Assert.Null(context.Result);
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Once);
    }

    [Fact]
    public async Task CookieAuth_WithInvalidToken_ShouldReturn400()
    {
        // Arrange
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("Invalid token"));

        var context = CreateContext("POST", isAuthenticated: true, authScheme: "Identity.Application");
        var attribute = new ValidateCsrfForCookiesAttribute();
        var executed = false;

        // Act
        await attribute.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Assert
        Assert.False(executed);
        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    [Fact]
    public async Task UnauthenticatedRequest_WithMutatingMethod_ShouldValidateCsrf()
    {
        // Arrange - Unauthenticated POST should still validate CSRF
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .Returns(Task.CompletedTask);

        var context = CreateContext("POST", isAuthenticated: false);
        var attribute = new ValidateCsrfForCookiesAttribute();

        // Act
        await attribute.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        // Assert
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Once);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task AllMutatingMethods_WithCookieAuth_ShouldValidateCsrf(string method)
    {
        // Arrange
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .Returns(Task.CompletedTask);

        var context = CreateContext(method, isAuthenticated: true, authScheme: "Identity.Application");
        var attribute = new ValidateCsrfForCookiesAttribute();

        // Act
        await attribute.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        // Assert
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Once);
    }

    [Theory]
    [InlineData("Identity.Application")]
    [InlineData("Identity.TwoFactorUserId")]
    [InlineData(null)]
    public async Task ForgedBearerHeader_ShouldNotBypassCsrf(string? cookieScheme)
    {
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("Missing token"));
        var context = CreateContext("POST", cookieScheme != null, cookieScheme);
        context.HttpContext.Request.Headers.Authorization = "Bearer invalid-token";
        var executed = false;

        await new ValidateCsrfForCookiesAttribute().OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.False(executed);
        Assert.IsType<BadRequestObjectResult>(context.Result);
        _antiforgeryMock.Verify(a => a.ValidateRequestAsync(context.HttpContext), Times.Once);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("JwtBearer")]
    [InlineData("AuthenticationTypes.Federation")]
    [InlineData("OpenIddict.Validation.AspNetCore")]
    public async Task AuthenticationTypeAlone_ShouldNotExemptAnUnvalidatedToken(string authenticationType)
    {
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("Missing token"));
        var context = CreateContext("POST", true, authenticationType, validatedBearer: false);
        var executed = false;

        await new ValidateCsrfForCookiesAttribute().OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.False(executed);
        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    [Theory]
    [InlineData("Identity.Application", false)]
    [InlineData("Identity.Application", true)]
    [InlineData("Identity.TwoFactorUserId", false)]
    [InlineData("Identity.TwoFactorUserId", true)]
    public async Task MixedBearerAndCookieIdentities_ShouldRequireCsrf(string cookieScheme, bool cookieFirst)
    {
        _antiforgeryMock.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("Missing token"));
        var context = CreateContext("POST", true, "Bearer");
        var bearerIdentity = context.HttpContext.User.Identities.Single();
        var cookieIdentity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "cookie-user")], cookieScheme);
        context.HttpContext.User = new ClaimsPrincipal(cookieFirst
            ? new[] { cookieIdentity, bearerIdentity }
            : new[] { bearerIdentity, cookieIdentity });
        var executed = false;

        await new ValidateCsrfForCookiesAttribute().OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.False(executed);
        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    private ActionExecutingContext CreateContext(string method, bool isAuthenticated, string? authScheme = null, bool? validatedBearer = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;

        // Setup DI
        var services = new ServiceCollection();
        services.AddSingleton(_antiforgeryMock.Object);
        var authentication = new Mock<IAuthenticationService>();
        var bearerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "token-user")], OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme));
        var bearerSucceeded = validatedBearer ?? authScheme is "Bearer" or "JwtBearer";
        authentication.Setup(a => a.AuthenticateAsync(httpContext, OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme))
            .ReturnsAsync(bearerSucceeded
                ? AuthenticateResult.Success(new AuthenticationTicket(bearerPrincipal, OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme))
                : AuthenticateResult.NoResult());
        services.AddSingleton(authentication.Object);
        httpContext.RequestServices = services.BuildServiceProvider();

        // Setup authentication
        if (isAuthenticated)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "testuser") },
                authScheme ?? "TestScheme");
            httpContext.User = new ClaimsPrincipal(identity);
        }

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            null!);
    }
}
