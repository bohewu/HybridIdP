using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderNativeProfileClientTests
{
    private const string Valid = """
        {"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"subject",
        "profile":{"displayName":"Display","email":"contact@example.test","assuredFields":["DisplayName","Email"]},
        "emailTrustOrigin":"PolicyTrusted"}
        """;
    private static ProviderNativeProfileRequest Request => new("1.0", "example.provider", "subject");
    private static ProviderNativeProfileOptions Options => new()
    {
        Enabled = true, Endpoint = "https://example.test/profile/native",
        TrustedProviderNamespace = "example.provider", SharedSecret = "fixture-secret"
    };

    [Fact]
    public async Task Fetch_ShouldSendOnlyExactTupleAndIndependentSecret()
    {
        using var handler = new Handler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://example.test/profile/native", request.RequestUri!.AbsoluteUri);
            Assert.Equal("fixture-secret", Assert.Single(request.Headers.GetValues("X-Internal-Secret")));
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(new[] { "contractVersion", "providerNamespace", "stableSubject" },
                document.RootElement.EnumerateObject().Select(x => x.Name));
            Assert.Equal("subject", document.RootElement.GetProperty("stableSubject").GetString());
            return Response(Valid);
        });
        using var http = new HttpClient(handler);
        var result = await new ProviderNativeProfileClient(http, Microsoft.Extensions.Options.Options.Create(Options)).FetchAsync(Request);
        Assert.Equal("Display", result.Profile.DisplayName);
        Assert.Equal("PolicyTrusted", result.EmailTrustOrigin);
        using var primary = ProviderNativeProfileClient.CreatePrimaryHandler();
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
        Assert.Null(primary.ServerCertificateCustomValidationCallback);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("profile_duplicate")]
    [InlineData("profile_unknown")]
    [InlineData("tuple")]
    [InlineData("version")]
    [InlineData("enum_case")]
    [InlineData("enum_number")]
    [InlineData("enum_duplicate")]
    [InlineData("trust_case")]
    [InlineData("unassured")]
    [InlineData("email")]
    [InlineData("length")]
    [InlineData("missing")]
    [InlineData("invalid_utf8")]
    public async Task Fetch_ShouldRejectMalformedOrUnassuredResponse(string scenario)
    {
        var body = scenario switch
        {
            "unknown" => Valid.Replace("\"contractVersion\"", "\"authenticated\":true,\"contractVersion\""),
            "duplicate" => Valid.Replace("\"contractVersion\"", "\"contractVersion\":\"1.0\",\"contractVersion\""),
            "profile_duplicate" => Valid.Replace("\"displayName\"", "\"displayName\":\"Other\",\"displayName\""),
            "profile_unknown" => Valid.Replace("\"displayName\"", "\"roles\":[],\"displayName\""),
            "tuple" => Valid.Replace("\"subject\"", "\"SUBJECT\""),
            "version" => Valid.Replace("\"1.0\"", "\"2.0\""),
            "enum_case" => Valid.Replace("\"DisplayName\"", "\"displayName\""),
            "enum_number" => Valid.Replace("\"DisplayName\"", "0"),
            "enum_duplicate" => Valid.Replace("\"DisplayName\"", "\"DisplayName\",\"DisplayName\""),
            "trust_case" => Valid.Replace("\"PolicyTrusted\"", "\"policyTrusted\""),
            "unassured" => Valid.Replace("\"DisplayName\",", ""),
            "email" => Valid.Replace("contact@example.test", "Display &lt;contact@example.test&gt;"),
            "length" => Valid.Replace("\"Display\"", "\"" + new string('a', 257) + "\""),
            "missing" => Valid.Replace("\"emailTrustOrigin\":\"PolicyTrusted\"", "\"profile\":{}"),
            _ => Valid
        };
        using var handler = new Handler((_, _) =>
        {
            var response = Response(body);
            if (scenario == "invalid_utf8")
            {
                var bytes = Encoding.UTF8.GetBytes(Valid);
                bytes[Array.IndexOf(bytes, (byte)'D')] = 0xff;
                response.Content = new ByteArrayContent(bytes);
                response.Content.Headers.ContentType = new("application/json");
            }
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        Assert.Equal("source_invalid_response", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            new ProviderNativeProfileClient(http, Microsoft.Extensions.Options.Options.Create(Options)).FetchAsync(Request))).Code);
    }

    [Theory]
    [InlineData(404, "application/json", "source_not_found")]
    [InlineData(503, "application/json", "source_unavailable")]
    [InlineData(302, "application/json", "source_unavailable")]
    [InlineData(201, "application/json", "source_unavailable")]
    [InlineData(200, "text/html", "source_invalid_response")]
    public async Task Fetch_ShouldEnforceStatusAndMedia(int status, string media, string expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent(Valid, Encoding.UTF8, media) }));
        using var http = new HttpClient(handler);
        Assert.Equal(expected, (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            new ProviderNativeProfileClient(http, Microsoft.Extensions.Options.Options.Create(Options)).FetchAsync(Request))).Code);
    }

    [Fact]
    public async Task Fetch_ShouldBoundBodyAndTimeoutAndPropagateCallerCancellation()
    {
        using var oversized = new Handler((_, _) => Task.FromResult(Response(new string(' ', 65537))));
        using var http = new HttpClient(oversized);
        Assert.Equal("source_unavailable", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            new ProviderNativeProfileClient(http, Microsoft.Extensions.Options.Options.Create(Options)).FetchAsync(Request))).Code);
        using var slow = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Response(Valid); });
        using var slowHttp = new HttpClient(slow);
        var options = Options;
        options.Timeout = TimeSpan.FromMilliseconds(20);
        var client = new ProviderNativeProfileClient(slowHttp, Microsoft.Extensions.Options.Options.Create(options));
        Assert.Equal("source_unavailable", (await Assert.ThrowsAsync<ProfileSyncException>(() => client.FetchAsync(Request))).Code);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(Request, canceled.Token));
    }

    [Fact]
    public async Task Fetch_ShouldFailClosedBeforeNetworkWhenDisabledUntrustedOrHttp()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Network must not be reached"));
        using var http = new HttpClient(handler);
        foreach (var options in new[] { new ProviderNativeProfileOptions(), Options, Options, Options })
        {
            if (options.Enabled && options.Endpoint!.StartsWith("https", StringComparison.Ordinal))
                options.Endpoint = "http://example.test/profile/native";
            Assert.Equal("configuration_invalid", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
                new ProviderNativeProfileClient(http, Microsoft.Extensions.Options.Options.Create(options)).FetchAsync(Request))).Code);
        }
        Assert.Equal("configuration_invalid", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            new ProviderNativeProfileClient(http, Microsoft.Extensions.Options.Options.Create(Options))
                .FetchAsync(Request with { ProviderNamespace = "EXAMPLE.PROVIDER" }))).Code);
    }

    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
