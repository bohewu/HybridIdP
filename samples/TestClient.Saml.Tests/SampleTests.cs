using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using TestClient.Saml.Controllers;
using TestClient.Saml.Protocol;
using Xunit;

namespace TestClient.Saml.Tests;

public sealed class SampleTests
{
    [Fact]
    public void RoundTrip_ShouldValidateSignedRequestAndBothResponseSignatures()
    {
        using var fixture = new Fixture();
        var proof = fixture.Consumer.Validate(fixture.Response());
        Assert.Equal(fixture.Request.RequestId, proof.RequestId);
        Assert.Equal(Fixture.Idp, proof.Issuer);
        Assert.Equal(Fixture.Subject.ToString("N"), proof.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Single(proof.Principal.Claims);
        var attribute = Assert.Single(proof.AttributeDisplay.Attributes);
        Assert.Equal("urn:hybrididp:claims:subject", attribute.Name);
        Assert.Equal(Fixture.Subject.ToString("N"), Assert.Single(attribute.Values));
        Assert.False(proof.AttributeDisplay.HasOmittedAttributes);
    }

    [Fact]
    public async Task Attributes_ShouldSurviveProtectedCookieAndRenderEncodedWithoutGrantingClaims()
    {
        using var fixture = new Fixture();
        const string hostileName = "<img src=x onerror=alert(1)>";
        const string hostileValue = "<script>alert('sample')</script> & received";
        var response = fixture.ResponseWithAttributes([
            new(hostileName, hostileValue), new("department", "Engineering"), new("department", "Research"),
            new(ClaimTypes.Role, "Administrator"), new("amr", "mfa"), new("validated_issuer", "spoofed")]);
        var proof = fixture.Consumer.Validate(response);
        Assert.Single(proof.Principal.Claims);
        Assert.False(proof.Principal.IsInRole("Administrator"));
        Assert.Equal(new[] { "Engineering", "Research" }, proof.AttributeDisplay.Attributes.Single(a => a.Name == "department").Values);

        fixture.PrepareConsume(response);
        Assert.Equal("/session", Assert.IsType<RedirectResult>(await fixture.Controller.ConsumeAsync()).Url);
        var ticket = await fixture.RestoreSessionAsync();
        Assert.Equal(3, ticket.Principal!.Claims.Count());
        Assert.False(ticket.Principal.IsInRole("Administrator"));
        Assert.Null(ticket.Principal.FindFirst("amr"));
        Assert.Equal(Fixture.Idp, ticket.Principal.FindFirstValue("validated_issuer"));
        var view = Assert.IsType<ViewResult>(await fixture.Controller.SessionAsync());
        var model = Assert.IsType<SamlAttributeDisplay>(view.Model);
        Assert.Equal(hostileValue, Assert.Single(model.Attributes.Single(a => a.Name == hostileName).Values));
        var html = await fixture.RenderAsync(view);
        Assert.Contains(HtmlEncoder.Default.Encode(hostileName), html);
        Assert.Contains(HtmlEncoder.Default.Encode(hostileValue), html);
        Assert.DoesNotContain(hostileName, html);
        Assert.DoesNotContain(hostileValue, html);
        Assert.Contains(Fixture.Subject.ToString("N"), html);
    }

    [Fact]
    public async Task Attributes_ShouldOmitOversizedEntriesWithoutChangingSignedIdentity()
    {
        using var fixture = new Fixture();
        var claims = new List<Claim>
        {
            new(new string('n', SamlAttributeDisplay.MaximumNameLength + 1), "name-too-long"),
            new("too-long", new string('v', SamlAttributeDisplay.MaximumValueLength + 1)),
            new("large-a", new string('a', 1000)), new("large-b", new string('b', 1000))
        };
        claims.AddRange(Enumerable.Range(0, 9).Select(i => new Claim("too-many-values", i.ToString())));
        claims.Add(new("small", "kept"));
        fixture.PrepareConsume(fixture.ResponseWithAttributes(claims));
        await fixture.Controller.ConsumeAsync();
        var ticket = await fixture.RestoreSessionAsync();
        var payload = ticket.Properties!.Items[SamlAttributeDisplay.TicketProperty]!;
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= SamlAttributeDisplay.MaximumPayloadBytes);
        var view = Assert.IsType<ViewResult>(await fixture.Controller.SessionAsync());
        var display = Assert.IsType<SamlAttributeDisplay>(view.Model);
        Assert.True(display.HasOmittedAttributes);
        Assert.DoesNotContain(display.Attributes, a => a.Name.Length > SamlAttributeDisplay.MaximumNameLength ||
            a.Name is "too-long" or "too-many-values");
        Assert.Single(display.Attributes.Where(a => a.Name.StartsWith("large-", StringComparison.Ordinal)));
        Assert.Equal("kept", Assert.Single(display.Attributes.Single(a => a.Name == "small").Values));
        Assert.Contains("Some attributes were omitted", await fixture.RenderAsync(view));
        Assert.Equal(Fixture.Subject.ToString("N"), ticket.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public void Attributes_ShouldBoundCountAndEscapedPayloadBytes()
    {
        using var fixture = new Fixture();
        var many = fixture.Consumer.Validate(fixture.ResponseWithAttributes(
            Enumerable.Range(0, 40).Select(i => new Claim("n" + i, "v"))));
        Assert.Equal(SamlAttributeDisplay.MaximumAttributes, many.AttributeDisplay.Attributes.Count);
        Assert.True(many.AttributeDisplay.HasOmittedAttributes);
        var escaped = fixture.Consumer.Validate(fixture.ResponseWithAttributes([new("escaped", new string('<', 1024))]));
        Assert.True(escaped.AttributeDisplay.HasOmittedAttributes);
        Assert.DoesNotContain(escaped.AttributeDisplay.Attributes, a => a.Name == "escaped");
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(escaped.AttributeDisplay).Length <= SamlAttributeDisplay.MaximumPayloadBytes);
    }

    [Fact]
    public async Task Consume_ShouldRejectTamperedAttributesAndMismatchedCorrelationBeforeIssuingCookie()
    {
        using var fixture = new Fixture();
        var signed = fixture.ResponseWithAttributes([new("department", "original-department")]);
        var tampered = Encoding.UTF8.GetString(Convert.FromBase64String(signed)).Replace("original-department", "tampered-department", StringComparison.Ordinal);
        fixture.PrepareConsume(Convert.ToBase64String(Encoding.UTF8.GetBytes(tampered)));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Controller.ConsumeAsync());
        Assert.Equal(0, fixture.Controller.Response.Headers.SetCookie.Count);

        fixture.PrepareConsume(fixture.ResponseWithAttributes([new("department", "valid")], "_different-request"));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Controller.ConsumeAsync());
        Assert.Equal(0, fixture.Controller.Response.Headers.SetCookie.Count);

        // Invalid attempts did not consume the pending request. One valid consumption still works.
        fixture.PrepareConsume(signed);
        await fixture.Controller.ConsumeAsync();
        fixture.PrepareConsume(signed);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Controller.ConsumeAsync());
        Assert.Equal(0, fixture.Controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Response_ShouldRejectTamperingAndUnpinnedSigningKey()
    {
        using var fixture = new Fixture();
        using var stranger = new Fixture();
        Assert.ThrowsAny<Exception>(() => fixture.Consumer.Validate(stranger.Response()));
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fixture.Response()));
        text = text.Replace(Fixture.Subject.ToString("N"), Guid.NewGuid().ToString("N"), StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => fixture.Consumer.Validate(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void Correlation_ShouldRejectCopiedUrlWithoutBrowserCookie()
    {
        var store = new BrowserStateStore(new EphemeralDataProtectionProvider());
        var context = new DefaultHttpContext();
        var token = BrowserStateStore.RandomToken();
        store.BindBrowser(context, token, "nonce", "/saml/sp/acs", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.False(store.MatchesBrowser(new DefaultHttpContext(), token, "nonce"));
        var browser = new DefaultHttpContext();
        browser.Request.Headers.Cookie = context.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.True(store.MatchesBrowser(browser, token, "nonce"));
        Assert.False(store.MatchesBrowser(browser, token, "other-nonce"));
    }

    [Fact]
    public async Task Correlation_ShouldHaveExactlyOneConcurrentConsumer()
    {
        var store = new BrowserStateStore(new EphemeralDataProtectionProvider());
        Assert.True(store.Add("correlation", "payload", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.False(store.Add("correlation", "replacement", DateTimeOffset.UtcNow.AddMinutes(5)));
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.Find<string>("correlation", true))));
        Assert.Equal(1, results.Count(value => value == "payload"));
        Assert.Null(store.Find<string>("correlation"));
        Assert.False(store.Add("expired", "payload", DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.False(store.Add("too-long", "payload", DateTimeOffset.UtcNow.AddMinutes(11)));
    }

    [Fact]
    public void DisabledRoles_ShouldNeedNoCertificatesAndRejectProtocolOperations()
    {
        using var runtime = new SamlFederationRuntime(new());
        Assert.Throws<InvalidOperationException>(() => runtime.CreateSpConfiguration());
        Assert.Throws<InvalidOperationException>(() => runtime.CreateIdpConfiguration("unknown"));
    }

    private sealed class Fixture : IDisposable
    {
        public const string Idp = "https://producer.example/idp";
        public const string Sp = "https://consumer.example/sp";
        public static readonly Guid Subject = new("fd294c09-5105-4c36-8833-5f9c288523cd");
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "saml-sample-test-" + Guid.NewGuid().ToString("N"));
        private readonly SamlFederationRuntime _runtime;
        private readonly WebApplication _app;
        private IServiceScope _scope;
        private readonly string _correlationCookie;
        public DemoController Controller { get; }
        public SamlIdpProtocol Producer { get; }
        public SamlSpResponseValidator Consumer { get; }
        public SamlIdpRequest Request { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            using var rsa = RSA.Create(2048);
            using var certificate = new CertificateRequest("CN=SAML Sample Fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var pfx = Path.Combine(_directory, "signing.pfx");
            var cer = Path.Combine(_directory, "signing.cer");
            File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pfx));
            File.WriteAllBytes(cer, certificate.Export(X509ContentType.Cert));
            _runtime = new(new SamlFederationOptions
            {
                Idp = new()
                {
                    Enabled = true, EntityId = Idp, SingleSignOnUrl = "https://producer.example/saml/idp/sso",
                    SigningCertificate = new() { PfxPath = pfx },
                    ServiceProviders = [new() { EntityId = Sp, AssertionConsumerServiceUrl = "https://consumer.example/saml/sp/acs", SigningCertificatePaths = [cer] }]
                },
                Sp = new()
                {
                    Enabled = true, EntityId = Sp, AssertionConsumerServiceUrl = "https://consumer.example/saml/sp/acs",
                    SigningCertificate = new() { PfxPath = pfx },
                    IdentityProvider = new() { EntityId = Idp, SingleSignOnUrl = "https://producer.example/saml/idp/sso", SigningCertificatePaths = [cer] }
                }
            });
            Producer = new(_runtime, TimeProvider.System);
            Consumer = new(_runtime, TimeProvider.System);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(DemoController).Assembly.FullName, EnvironmentName = "Test"
            });
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(DemoController).Assembly);
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
            _app = builder.Build(); // No listener: exercise the real cookie handler and compiled Razor view in-process.
            _scope = _app.Services.CreateScope();
            Controller = new DemoController(_runtime, new(new EphemeralDataProtectionProvider()), Producer, Consumer, new())
            { ControllerContext = new() { HttpContext = NewContext() } };
            var location = Assert.IsType<RedirectResult>(Controller.Login()).Url;
            _correlationCookie = Controller.Response.Headers.SetCookie.ToString().Split(';')[0];
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Method = "GET";
            http.Request.QueryString = new QueryString(new Uri(location).Query);
            Request = Producer.ReadRequest(http.Request);
        }

        public string Response() => Producer.CreateResponse(Request, Subject, DateTimeOffset.UtcNow);

        public string ResponseWithAttributes(IEnumerable<Claim> attributes, string? requestId = null)
        {
            var subject = Subject.ToString("N");
            var response = new Saml2AuthnResponse(_runtime.CreateIdpConfiguration(Sp))
            {
                InResponseTo = new Microsoft.IdentityModel.Tokens.Saml2.Saml2Id(requestId ?? Request.RequestId), Status = Saml2StatusCodes.Success,
                Destination = new Uri(_runtime.Options.Sp.AssertionConsumerServiceUrl),
                NameId = new Microsoft.IdentityModel.Tokens.Saml2.Saml2NameIdentifier(subject, new Uri(SamlProfile.PersistentNameIdFormat))
                { NameQualifier = Idp, SPNameQualifier = Sp },
                SessionIndex = Guid.NewGuid().ToString("N"),
                ClaimsIdentity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim("urn:hybrididp:claims:subject", subject) }.Concat(attributes))
            };
            response.CreateSecurityToken(Sp, authnContext: new Uri("urn:oasis:names:tc:SAML:2.0:ac:classes:unspecified"),
                subjectConfirmationLifetime: 5, issuedTokenLifetime: 5);
            var binding = new Saml2PostBinding();
            binding.Bind(response);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(binding.XmlDocument.OuterXml));
        }

        public void PrepareConsume(string response)
        {
            _scope.Dispose();
            _scope = _app.Services.CreateScope();
            Controller.ControllerContext = new() { HttpContext = NewContext() };
            Controller.Request.Method = "POST";
            Controller.Request.ContentType = "application/x-www-form-urlencoded";
            Controller.Request.Headers.Cookie = _correlationCookie;
            Controller.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            { ["SAMLResponse"] = response, ["RelayState"] = Request.RelayState! });
        }

        public async Task<AuthenticateResult> RestoreSessionAsync()
        {
            var cookies = string.Join("; ", Controller.Response.Headers.SetCookie.Select(c => c!.Split(';')[0]));
            // Authentication handlers are request-scoped; use a fresh scope for the reload.
            _scope.Dispose();
            _scope = _app.Services.CreateScope();
            Controller.ControllerContext = new() { HttpContext = NewContext() };
            Controller.Request.Headers.Cookie = cookies;
            var ticket = await Controller.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Assert.True(ticket.Succeeded);
            Controller.HttpContext.User = ticket.Principal!;
            return ticket;
        }

        public async Task<string> RenderAsync(ViewResult result)
        {
            var view = _scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
                .GetView(null, "/Views/Demo/Session.cshtml", isMainPage: true);
            Assert.True(view.Success, string.Join(", ", view.SearchedLocations ?? []));
            using var writer = new StringWriter();
            Controller.ControllerContext.RouteData = new Microsoft.AspNetCore.Routing.RouteData();
            Controller.ControllerContext.ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor();
            var context = new ViewContext(Controller.ControllerContext, view.View, result.ViewData,
                new TempDataDictionary(Controller.HttpContext, _scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),
                writer, new HtmlHelperOptions());
            await view.View.RenderAsync(context);
            return writer.ToString();
        }

        private DefaultHttpContext NewContext()
        {
            var context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("consumer.example");
            return context;
        }

        public void Dispose()
        {
            _scope.Dispose();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _runtime.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
