using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Session;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Web.IdP;
using Web.IdP.Middleware;
using Web.IdP.Services;
using Xunit.Abstractions;
using Web.IdP.Helpers;

namespace Tests.SystemTests;

// Bounded in-process hosts and injected cache failures; no Redis/DB/browser is started.
public sealed class SessionAvailabilityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Framework_SynchronousReadFailureLooksMissing_ButExplicitLoadThrows()
    {
        using var fixture = new Fixture();
        fixture.Cache.FailRead = true;
        using var synchronous = await fixture.Client.GetAsync("/sync");
        Assert.Equal("missing", await synchronous.Content.ReadAsStringAsync());
        using var explicitLoad = await fixture.Client.GetAsync("/load");
        Assert.Equal(503, (int)explicitLoad.StatusCode);
        Assert.Equal(1, fixture.Cache.SyncReads);
        Assert.Equal(1, fixture.Cache.AsyncReads);
        foreach (var type in new[] { typeof(SessionMiddleware), typeof(DistributedSession),
                     typeof(Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache), typeof(TestServer) })
        {
            var assembly = type.Assembly;
            output.WriteLine($"{assembly.GetName().Name}: {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}; {assembly.Location}");
        }
    }

    [Fact]
    public async Task Framework_EndOfRequestCommitFailureDoesNotRetractSuccess()
    {
        using var fixture = new Fixture();
        fixture.Cache.FailWrite = true;
        using var response = await fixture.Client.GetAsync("/begin");
        Assert.Equal(302, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, fixture.Cache.Writes);
        Assert.Empty(fixture.Cache.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredState_ShouldStopBeforeCompletion_WhenReadOrWriteCannotBeConfirmed(bool writing)
    {
        using var fixture = new Fixture(confirmed: true);
        fixture.Cache.FailRead = !writing;
        fixture.Cache.FailWrite = writing;
        using var response = await fixture.Client.GetAsync("/begin");
        Assert.Equal(503, (int)response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("fixture-auth="));
        Assert.Contains("try again", await response.Content.ReadAsStringAsync());
        Assert.Empty(fixture.Cache.Values);
        Assert.Equal(writing ? 1 : 0, fixture.Cache.Writes);
        Assert.Equal(0, fixture.Cache.SyncReads);
    }

    [Fact]
    public async Task RequiredState_ShouldRestoreWithNormalStampChecks_AndConfirmBeforeReturningProof()
    {
        using var fixture = new Fixture(confirmed: true);
        using var started = await fixture.Client.GetAsync("/begin");
        var cookie = started.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".AspNetCore.Session="))
            .Split(';')[0];
        fixture.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        fixture.Cache.FailRead = true;
        using var unavailable = await fixture.Client.GetAsync("/complete");
        Assert.Equal(503, (int)unavailable.StatusCode);
        fixture.Cache.FailRead = false;
        fixture.Cache.FailWrite = true;
        using var unconfirmed = await fixture.Client.GetAsync("/complete");
        Assert.Equal(503, (int)unconfirmed.StatusCode);
        Assert.DoesNotContain(unconfirmed.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("fixture-auth="));
        fixture.Cache.FailWrite = false;
        using var completed = await fixture.Client.GetAsync("/complete");
        Assert.Equal(200, (int)completed.StatusCode);
        Assert.Equal("confirmed", await completed.Content.ReadAsStringAsync());
        using var replay = await fixture.Client.GetAsync("/complete");
        Assert.Equal(403, (int)replay.StatusCode);

        using var restart = await fixture.Client.GetAsync("/begin");
        using var wrongStamp = await fixture.Client.GetAsync("/complete?invalid=true");
        Assert.Equal(403, (int)wrongStamp.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredState_ShouldBoundUncooperativeCacheIo_WithoutMiddlewareRetry(bool writing)
    {
        using var fixture = new Fixture(confirmed: true);
        fixture.Cache.HangRead = !writing;
        fixture.Cache.HangWrite = writing;
        var timer = Stopwatch.StartNew();
        using var response = await fixture.Client.GetAsync("/begin");
        timer.Stop();
        output.WriteLine($"Injected {(writing ? "write" : "read")} delay: {timer.Elapsed.TotalMilliseconds:F0} ms; configured IO bound {ConfirmedSessionStore.IoTimeout.TotalSeconds}s");
        Assert.Equal(503, (int)response.StatusCode);
        Assert.InRange(timer.Elapsed.TotalSeconds, 4, 8);
        Assert.Equal(writing ? 1 : 0, fixture.Cache.Writes);
        Assert.Empty(fixture.Cache.Values);
    }

    [Fact]
    public async Task UnusedSession_ShouldNotReadOrWriteCache_OrSuppressCookieDeletion()
    {
        using var fixture = new Fixture(confirmed: true);
        fixture.Cache.FailRead = fixture.Cache.FailWrite = true;
        using var response = await fixture.Client.GetAsync("/untouched");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("fixture-auth=;"));
        Assert.Equal(0, fixture.Cache.AsyncReads + fixture.Cache.SyncReads + fixture.Cache.Writes);
    }

    [Theory]
    [InlineData("/api/begin")]
    [InlineData("/connect/begin")]
    public async Task RequiredState_ShouldKeepApiAndProtocolFailureMachineReadable(string path)
    {
        using var fixture = new Fixture(confirmed: true);
        fixture.Cache.FailRead = true;
        using var response = await fixture.Client.GetAsync(path);
        Assert.Equal(503, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("temporarily_unavailable", json.RootElement.GetProperty("error").GetString());
        Assert.Contains("try again", json.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task RequiredCleanup_ShouldDiscardNewAuthenticationCookie_WhenConfirmationFailsAfterSignIn()
    {
        using var fixture = new Fixture(confirmed: true);
        using var response = await fixture.Client.GetAsync("/late-completion");
        Assert.Equal(503, (int)response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("fixture-auth="));
        Assert.Contains("try again", await response.Content.ReadAsStringAsync());
    }

    private sealed class Fixture : IDisposable
    {
        public FaultCache Cache { get; } = new();
        public TestServer Server { get; }
        public HttpClient Client { get; }
        private readonly Guid _userId = Guid.NewGuid();
        public Fixture(bool confirmed = false)
        {
            Server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IDistributedCache>(Cache);
                    services.AddSession(options => options.IOTimeout = TimeSpan.FromSeconds(5));
                    services.AddAuthentication("fixture").AddCookie("fixture", options => options.Cookie.Name = "fixture-auth");
                    services.AddLocalization();
                    services.AddSingleton<IStringLocalizer<SharedResource>, RetryLocalizer>();
                    if (confirmed) services.AddTransient<ISessionStore, ConfirmedSessionStore>();
                })
                .Configure(app =>
                {
                    app.UseSession();
                    if (confirmed) app.UseMiddleware<SessionStateUnavailableMiddleware>();
                    app.UseAuthentication();
                    app.Run(async http =>
                    {
                        switch (http.Request.Path.Value)
                        {
                            case "/sync":
                                await http.Response.WriteAsync(http.Session.GetString("proof") ?? "missing");
                                break;
                            case "/load":
                                try { await http.Session.LoadAsync(); }
                                catch (IOException) { http.Response.StatusCode = 503; }
                                break;
                            case "/begin":
                            case "/api/begin":
                            case "/connect/begin":
                                MfaEnrollmentSession.Begin(http.Session, _userId, securityStamp: "fixture-stamp");
                                http.Response.Cookies.Append("fixture-auth", "completion");
                                http.Response.Redirect("/complete");
                                break;
                            case "/complete":
                                var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
                                {
                                    new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                                    new Claim("AspNet.Identity.SecurityStamp", http.Request.Query.ContainsKey("invalid") ? "wrong" : "fixture-stamp")
                                }, "fixture"));
                                // A queued cookie must also be discarded if a later required write fails.
                                http.Response.Cookies.Append("fixture-auth", "completion");
                                if (!MfaEnrollmentSession.CompletePending(http.Session, principal))
                                    http.Response.StatusCode = 403;
                                else await http.Response.WriteAsync("confirmed");
                                break;
                            case "/untouched":
                                http.Response.Cookies.Delete("fixture-auth");
                                await http.Response.WriteAsync("independent");
                                break;
                            case "/late-completion":
                                MfaEnrollmentSession.Begin(http.Session, _userId, securityStamp: "fixture-stamp");
                                await http.SignInAsync("fixture", new ClaimsPrincipal(new ClaimsIdentity(
                                    [new Claim(ClaimTypes.NameIdentifier, _userId.ToString())], "fixture")));
                                Assert.Contains(http.Response.Headers.SetCookie, value => value!.StartsWith("fixture-auth="));
                                Cache.FailWrite = true;
                                MfaEnrollmentSession.Consume(http.Session);
                                http.Response.Redirect("/complete");
                                break;
                        }
                    });
                }));
            Client = Server.CreateClient();
        }
        public void Dispose() { Cache.Release(); Client.Dispose(); Server.Dispose(); }
    }

    private sealed class RetryLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, "Wait, then try again.");
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class FaultCache : IDistributedCache
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public bool HangRead { get; set; }
        public bool HangWrite { get; set; }
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();
        public int SyncReads { get; private set; }
        public int AsyncReads { get; private set; }
        public int Writes { get; private set; }
        public byte[]? Get(string key)
        {
            SyncReads++;
            if (FailRead) throw new IOException("Injected cache read failure");
            return Values.GetValueOrDefault(key);
        }
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            AsyncReads++;
            if (FailRead) throw new IOException("Injected cache read failure");
            if (HangRead) await _release.Task;
            return Values.GetValueOrDefault(key);
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Writes++;
            if (FailWrite) throw new IOException("Injected cache write failure");
            Values[key] = value.ToArray();
        }
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (HangWrite) { Writes++; await _release.Task; return; }
            Set(key, value, options);
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        { Remove(key); return Task.CompletedTask; }
    }
}
