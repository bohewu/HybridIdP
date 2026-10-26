using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TestClient.Saml.Protocol;

namespace TestClient.Saml.Controllers;

public sealed class DemoController(SamlFederationRuntime runtime, BrowserStateStore state,
    SamlIdpProtocol producer, SamlSpResponseValidator consumer, DemoIdentityOptions demo) : Controller
{
    private const string Acs = "/saml/sp/acs";
    private const string Continue = "/saml/idp/continue";

    [HttpGet("/")]
    public IActionResult Index() => View(runtime.Options);

    [HttpPost("/saml/sp/login"), ValidateAntiForgeryToken]
    public IActionResult Login()
    {
        if (!runtime.Options.Sp.Enabled) return NotFound();
        var request = new Saml2AuthnRequest(runtime.CreateSpConfiguration())
        {
            AssertionConsumerServiceUrl = new Uri(runtime.Options.Sp.AssertionConsumerServiceUrl),
            ProtocolBinding = ProtocolBindings.HttpPost,
            NameIdPolicy = new NameIdPolicy { Format = SamlProfile.PersistentNameIdFormat, AllowCreate = true }
        };
        var relay = BrowserStateStore.RandomToken();
        var pending = new ConsumerRequest(request.IdAsString, BrowserStateStore.RandomToken());
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        Require(state.Add("sp:" + relay, pending, expires));
        state.BindBrowser(HttpContext, relay, pending.Nonce, Acs, expires);
        var binding = new Saml2RedirectBinding { RelayState = relay };
        binding.Bind(request);
        return Redirect(binding.RedirectLocation.OriginalString);
    }

    [HttpPost(Acs), IgnoreAntiforgeryToken, RequestSizeLimit(256 * 1024), RequestFormLimits(ValueLengthLimit = 180000)]
    public async Task<IActionResult> ConsumeAsync()
    {
        if (!runtime.Options.Sp.Enabled) return NotFound();
        Require(Request.HasFormContentType);
        var form = await Request.ReadFormAsync();
        Require(form.Count == 2 && form.Files.Count == 0 && form["SAMLResponse"].Count == 1 && form["RelayState"].Count == 1);
        var relay = form["RelayState"].ToString();
        Require(ValidToken(relay));
        var pending = state.Find<ConsumerRequest>("sp:" + relay);
        Require(pending != null && state.MatchesBrowser(HttpContext, relay, pending.Nonce));
        var proof = consumer.Validate(form["SAMLResponse"].ToString());
        Require(proof.RequestId == pending!.RequestId);
        Require(state.Find<ConsumerRequest>("sp:" + relay, take: true) == pending);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        Require(state.Add(ReplayKey("response", proof.Issuer, proof.ResponseId), "reserved", expires));
        Require(state.Add(ReplayKey("assertion", proof.Issuer, proof.AssertionId), "reserved", expires));
        state.ClearBrowser(HttpContext, relay, Acs);
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, proof.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!));
        identity.AddClaim(new Claim("validated_issuer", proof.Issuer));
        identity.AddClaim(new Claim("validated_at", DateTimeOffset.UtcNow.ToString("u")));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        return Redirect("/session");
    }

    [HttpGet("/session")]
    public IActionResult Session() => !runtime.Options.Sp.Enabled ? NotFound() : View();

    [HttpPost("/logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [HttpGet("/saml/idp/sso")]
    public IActionResult SingleSignOn()
    {
        if (!runtime.Options.Idp.Enabled) return NotFound();
        var request = producer.ReadRequest(Request);
        Require(state.Add(ReplayKey("request", request.Partner, request.RequestId), "reserved", request.ExpiresAt));
        var token = BrowserStateStore.RandomToken();
        var pending = new ProducerRequest(request, BrowserStateStore.RandomToken());
        Require(state.Add("idp:" + token, pending, request.ExpiresAt));
        state.BindBrowser(HttpContext, token, pending.Nonce, Continue, request.ExpiresAt);
        return Redirect(Continue + "?state=" + token);
    }

    [HttpGet(Continue)]
    public IActionResult Authenticate(string state)
    {
        if (!runtime.Options.Idp.Enabled) return NotFound();
        _ = PendingProducer(state);
        return View("Authenticate", new ProducerLoginModel { State = state });
    }

    [HttpPost(Continue), ValidateAntiForgeryToken, EnableRateLimiting("demo-password")]
    public IActionResult Authenticate(ProducerLoginModel model)
    {
        if (!runtime.Options.Idp.Enabled) return NotFound();
        var pending = PendingProducer(model.State);
        if (!EqualSecret(model.Username, demo.Username) || !EqualSecret(model.Password, demo.Password))
        {
            ModelState.AddModelError("", "The configured demo username or password is incorrect.");
            model.Password = "";
            return View("Authenticate", model);
        }
        Require(state.Find<ProducerRequest>("idp:" + model.State, take: true) == pending);
        state.ClearBrowser(HttpContext, model.State, Continue);
        var response = producer.CreateResponse(pending.Request, Guid.ParseExact(demo.Subject, "N"), DateTimeOffset.UtcNow);
        var partner = runtime.Options.Idp.ServiceProviders.Single(p => p.EntityId == pending.Request.Partner);
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; script-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action " + new Uri(partner.AssertionConsumerServiceUrl).GetLeftPart(UriPartial.Authority);
        return View("PostResponse", new ProducerResponseModel(partner.AssertionConsumerServiceUrl, response, pending.Request.RelayState));
    }

    [HttpGet("/saml/idp/metadata")]
    public IActionResult IdpMetadata() => !runtime.Options.Idp.Enabled ? NotFound() : Content(producer.Metadata(), "application/samlmetadata+xml");

    [HttpGet("/saml/sp/metadata")]
    public IActionResult SpMetadata()
    {
        if (!runtime.Options.Sp.Enabled) return NotFound();
        _ = runtime.CreateSpConfiguration();
        XNamespace md = "urn:oasis:names:tc:SAML:2.0:metadata";
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        var xml = new XElement(md + "EntityDescriptor", new XAttribute("entityID", runtime.Options.Sp.EntityId),
            new XElement(md + "SPSSODescriptor", new XAttribute("protocolSupportEnumeration", "urn:oasis:names:tc:SAML:2.0:protocol"),
                new XAttribute("AuthnRequestsSigned", "true"), new XAttribute("WantAssertionsSigned", "true"),
                new XElement(md + "KeyDescriptor", new XAttribute("use", "signing"),
                    new XElement(ds + "KeyInfo", new XElement(ds + "X509Data", new XElement(ds + "X509Certificate",
                        Convert.ToBase64String(runtime.GetSpSigningCertificate().RawData))))),
                new XElement(md + "NameIDFormat", SamlProfile.PersistentNameIdFormat),
                new XElement(md + "AssertionConsumerService", new XAttribute("Binding", ProtocolBindings.HttpPost.OriginalString),
                    new XAttribute("Location", runtime.Options.Sp.AssertionConsumerServiceUrl), new XAttribute("index", "0"))));
        return Content(xml.ToString(SaveOptions.DisableFormatting), "application/samlmetadata+xml");
    }

    private ProducerRequest PendingProducer(string token)
    {
        Require(ValidToken(token));
        var pending = state.Find<ProducerRequest>("idp:" + token);
        Require(pending != null && state.MatchesBrowser(HttpContext, token, pending.Nonce));
        return pending!;
    }

    private static bool EqualSecret(string? value, string expected) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    private static bool ValidToken(string? value) => value?.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string ReplayKey(string purpose, string issuer, string id) => purpose + ":" + issuer.Length + ":" + issuer + id;
    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("SAML exchange rejected."); }
    private sealed record ConsumerRequest(string RequestId, string Nonce);
    private sealed record ProducerRequest(SamlIdpRequest Request, string Nonce);
}

public sealed class ProducerLoginModel
{
    public string State { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public sealed record ProducerResponseModel(string Destination, string Response, string? RelayState);
