using System.Collections.Concurrent;
using Core.Application.Interfaces;
using Core.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Web.IdP;
using Web.IdP.Controllers.Saml;
using Web.IdP.Services.Saml;
using Web.IdP.Services.Saml.Idp;

namespace Tests.Web.IdP.UnitTests;

public sealed class SamlIdpFlowTests
{
    [Fact]
    public async Task DisabledEndpoints_ShouldReturn404WithoutResolvingEnabledServices()
    {
        using var runtime = new SamlFederationRuntime(new());
        var controller = new SamlIdpController(runtime, NullLogger<SamlIdpController>.Instance, Mock.Of<IStringLocalizer<SharedResource>>())
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<NotFoundResult>(controller.Metadata());
        Assert.IsType<NotFoundResult>(await controller.SingleSignOn(default));
        Assert.IsType<NotFoundResult>(await controller.Continue(null, default));
        Assert.IsType<NotFoundResult>(controller.PostScript());
    }

    [Fact]
    public async Task Continue_ShouldRequireBrowserCookieAndConsumeOnlyAfterLocalPolicy()
    {
        using var fixture = new SamlIdpFixture();
        var policy = new Mock<ISamlIdpAccountPolicy>();
        var user = Guid.NewGuid();
        await using var db = SamlAttributeTests.CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        db.Users.Add(new ApplicationUser { Id = user });
        await db.SaveChangesAsync();
        policy.Setup(p => p.EvaluateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SamlIdpAccountResult(user, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var flow = new SamlIdpFlow(fixture.Runtime, fixture.Protocol, new StateStore(), new EphemeralDataProtectionProvider(), policy.Object, TimeProvider.System,
            SamlAttributeTests.CreateService(fixture, db, cache));
        var beginHttp = fixture.Request().HttpContext;
        var redirect = Assert.IsType<RedirectResult>(await flow.BeginAsync(beginHttp, default));
        var state = QueryHelpers.ParseQuery(new Uri("https://hybrid.example" + redirect.Url).Query)["state"].ToString();
        var cookie = beginHttp.Response.Headers.SetCookie.ToString().Split(';')[0];
        var continuation = new DefaultHttpContext();
        continuation.Request.Scheme = "https";
        Assert.IsType<BadRequestResult>(await flow.ContinueAsync(continuation, state, "Next", default));
        policy.Verify(p => p.EvaluateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        continuation.Request.Headers.Cookie = cookie;
        var result = Assert.IsType<ContentResult>(await flow.ContinueAsync(continuation, state, "Next", default));
        Assert.Contains("/saml/idp/post.js", result.Content);
        Assert.DoesNotContain("<script>", result.Content);
        Assert.Equal("form-action https://partner.example/acs; base-uri 'none'; frame-ancestors 'none'", continuation.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.IsType<BadRequestResult>(await flow.ContinueAsync(continuation, state, "Next", default));
        Assert.IsType<BadRequestResult>(await flow.BeginAsync(beginHttp, default));
    }

    [Fact]
    public async Task Continue_ShouldRetainStateForLoginButDenyIfEligibilityChangesBeforeIssuance()
    {
        using var fixture = new SamlIdpFixture();
        var policy = new Mock<ISamlIdpAccountPolicy>();
        var user = Guid.NewGuid();
        await using var db = SamlAttributeTests.CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        policy.SetupSequence(p => p.EvaluateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SamlIdpAccountResult(Guid.Empty, default, new ChallengeResult()))
            .ReturnsAsync(new SamlIdpAccountResult(user, DateTimeOffset.UtcNow))
            .ReturnsAsync(new SamlIdpAccountResult(Guid.Empty, default, new StatusCodeResult(403)));
        var flow = new SamlIdpFlow(fixture.Runtime, fixture.Protocol, new StateStore(), new EphemeralDataProtectionProvider(), policy.Object, TimeProvider.System,
            SamlAttributeTests.CreateService(fixture, db, cache));
        var begin = fixture.Request().HttpContext;
        var redirect = Assert.IsType<RedirectResult>(await flow.BeginAsync(begin, default));
        var state = QueryHelpers.ParseQuery(new Uri("https://hybrid.example" + redirect.Url).Query)["state"].ToString();
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Headers.Cookie = begin.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.IsType<ChallengeResult>(await flow.ContinueAsync(http, state, "Next", default));
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await flow.ContinueAsync(http, state, "Next", default)).StatusCode);
    }

    private sealed class StateStore : ISamlStateStore
    {
        private readonly ConcurrentDictionary<(SamlStatePurpose, string), string> _states = new();
        public Task<bool> TryAddAsync(SamlStatePurpose purpose, string key, string payload, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.TryAdd((purpose, key), payload));
        public Task<string?> TryTakeAsync(SamlStatePurpose purpose, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.TryRemove((purpose, key), out var payload) ? payload : null);
    }
}
