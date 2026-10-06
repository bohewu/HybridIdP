using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Tests.Infrastructure.IntegrationTests;

// A separate synthetic HTTP producer process, never the deployment producer or a source adapter.
public sealed class RecoveryIdentityVerificationHttpTests
{
    private static RecoveryIdentityVerificationRequest Request(string evidence = " test-ID-0001 ") => new()
    {
        RequestId = "73aab4b5-2dfa-471a-8c2b-1d70d7540092",
        ProviderNamespace = "example.provider",
        StableSubject = "synthetic-subject-001",
        Evidence = new() { IdentityIdentifier = evidence }
    };

    [Fact]
    public async Task VerifyAsync_ShouldEnforceCanonicalEnvelopesAcrossProcesses_WithoutRedirectOrRetry()
    {
        await using var producer = await SyntheticProducer.StartAsync();
        using var http = new HttpClient(RecoveryIdentityVerificationClient.CreatePrimaryHandler());
        foreach (var fixture in producer.Cases)
        {
            var client = CreateClient(http, producer, fixture.Key);
            var result = await client.VerifyAsync(Request());
            Assert.Equal(fixture.Value.Accepts, result is not null);
            if (result is not null)
                Assert.Equal(fixture.Value.Outcome, result.Outcome.ToString());
        }

        // C18/C21: consumer sends unchanged evidence; the same correlation never caches a result.
        var repeat = CreateClient(http, producer, "C01");
        Assert.Equal(RecoveryIdentityVerificationOutcome.Verified, (await repeat.VerifyAsync(Request()))!.Outcome);
        using var stats = JsonDocument.Parse(await http.GetStringAsync(new Uri(producer.Address, "stats")));
        Assert.Equal(producer.Cases.Count + 1, stats.RootElement.GetProperty("calls").GetInt32());
        Assert.Equal(0, stats.RootElement.GetProperty("redirects").GetInt32());
        Assert.Equal(0, stats.RootElement.GetProperty("invalidRequests").GetInt32());
    }

    [Fact]
    public async Task VerifyAsync_ShouldBoundBodyDeadlineAndPropagateCancellation_AcrossProcesses()
    {
        await using var producer = await SyntheticProducer.StartAsync();
        using var http = new HttpClient(RecoveryIdentityVerificationClient.CreatePrimaryHandler());
        var client = CreateClient(http, producer, "slow");
        var elapsed = Stopwatch.StartNew();
        Assert.Null(await client.VerifyAsync(Request()));
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(4));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyAsync(Request(), cancellation.Token));
    }

    private static RecoveryIdentityVerificationClient CreateClient(HttpClient http, SyntheticProducer producer, string path)
    {
        // Reuse the production client's existing test-only loopback seam; do not widen production visibility/configuration.
        var factory = typeof(RecoveryIdentityVerificationClient).GetMethod("CreateForLoopbackTest",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (RecoveryIdentityVerificationClient)factory.Invoke(null, [http, Options.Create(new RecoveryIdentityVerificationOptions
        {
            Enabled = true, RequireForDirectoryAccounts = true,
            Endpoint = new Uri(producer.Address, path).AbsoluteUri,
            SharedSecret = producer.Secret, TimeoutSeconds = 1
        })])!;
    }

    private sealed record Envelope(int Status, string ContentType, string Body, bool Accepts, string? Outcome);

    private sealed class SyntheticProducer : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _directory;
        private bool _started;
        public Uri Address { get; }
        public string Secret { get; } = Guid.NewGuid().ToString("N");
        public Dictionary<string, Envelope> Cases { get; } = [];

        private SyntheticProducer(Process process, string directory, Uri address)
        {
            _process = process;
            _directory = directory;
            Address = address;
        }

        public static async Task<SyntheticProducer> StartAsync()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "HybridAuthIdP.sln"))) root = root.Parent;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var directory = Path.Combine(Path.GetTempPath(), $"riv-http-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var process = new Process { StartInfo = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            }};
            var producer = new SyntheticProducer(process, directory, new Uri($"http://127.0.0.1:{port}/"));
            return await producer.LaunchAsync(root!.FullName);
        }

        private async Task<SyntheticProducer> LaunchAsync(string root)
        {
            foreach (var number in new[] { 1, 2, 3, 4, 5, 6, 7, 13, 14, 15, 16, 17, 20, 22, 27 })
            {
                using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,
                    "docs/examples/recovery-identity-verification", $"C{number:00}.json")));
                var item = fixture.RootElement;
                var body = item.GetProperty("responseJson").GetString()!;
                var accepts = item.GetProperty("consumerAcceptsEnvelope").GetBoolean();
                using var parsed = accepts ? JsonDocument.Parse(body) : null;
                Cases[$"C{number:00}"] = new(item.GetProperty("httpStatus").GetInt32(),
                    item.GetProperty("contentType").GetString()!, body, accepts,
                    parsed?.RootElement.GetProperty("outcome").GetString());
                if (item.TryGetProperty("responseVariants", out var variants))
                    foreach (var variant in variants.EnumerateArray())
                        Cases[$"C{number:00}-{Cases.Count}"] = new(200, "application/json", variant.GetProperty("json").GetString()!, false, null);
            }
            var path = Path.Combine(_directory, "envelopes.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Cases));
            _process.StartInfo.Environment["RIV_CASES"] = path;
            _process.StartInfo.Environment["RIV_URI"] = Address.AbsoluteUri;
            _process.StartInfo.Environment["RIV_SECRET"] = Secret;
            _process.StartInfo.ArgumentList.Add("-NoProfile");
            _process.StartInfo.ArgumentList.Add("-NonInteractive");
            _process.StartInfo.ArgumentList.Add("-EncodedCommand");
            _process.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(ProducerScript)));
            try
            {
                _process.Start();
                _started = true;
                Assert.Equal("READY", await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
                return this;
            }
            catch
            {
                await DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_started && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (_started) Assert.True(_process.HasExited);
            _process.Dispose();
            Directory.Delete(_directory, recursive: true);
            Assert.False(Directory.Exists(_directory));
        }

        private const string ProducerScript = """
            $ErrorActionPreference='Stop'
            $cases=Get-Content -Raw -LiteralPath $env:RIV_CASES | ConvertFrom-Json -AsHashtable
            $listener=[System.Net.HttpListener]::new()
            $listener.Prefixes.Add($env:RIV_URI)
            $listener.Start()
            $calls=0; $redirects=0; $invalidRequests=0
            [Console]::WriteLine('READY')
            try {
                while ($listener.IsListening) {
                    $context=$listener.GetContext(); $request=$context.Request; $response=$context.Response
                    $route=$request.Url.AbsolutePath.TrimStart('/')
                    if ($route -eq 'stats') {
                        $body=@{calls=$calls; redirects=$redirects; invalidRequests=$invalidRequests} | ConvertTo-Json -Compress
                    } else {
                        if ($route -eq 'redirect-target') { $redirects++ }
                        $calls++
                        $reader=[System.IO.StreamReader]::new($request.InputStream,[System.Text.Encoding]::UTF8)
                        $wire=$reader.ReadToEnd(); $reader.Dispose()
                        $parsed=$wire | ConvertFrom-Json
                        if ($request.HttpMethod -ne 'POST' -or $request.ContentType -ne 'application/json' -or
                            $request.Headers['X-Internal-Secret'] -ne $env:RIV_SECRET -or $request.Url.Query -ne '' -or
                            $parsed.evidence.identityIdentifier -cne ' test-ID-0001 ' -or $wire.Contains($env:RIV_SECRET)) {
                            $invalidRequests++
                        }
                        if ($route -eq 'slow') {
                            $response.ContentType='application/json'; $response.SendChunked=$true
                            $response.OutputStream.WriteByte(123); $response.OutputStream.Flush()
                            Start-Sleep -Milliseconds 2200
                            try { $response.Close() } catch {}
                            continue
                        }
                        $case=$cases[$route]; $response.StatusCode=$case.Status; $response.ContentType=$case.ContentType; $body=$case.Body
                        if ($route -eq 'C17') { $response.RedirectLocation=$env:RIV_URI+'redirect-target' }
                    }
                    $response.Headers['Cache-Control']='no-store'
                    if (-not $response.ContentType) { $response.ContentType='application/json' }
                    $bytes=[System.Text.Encoding]::UTF8.GetBytes($body)
                    $response.ContentLength64=$bytes.Length
                    try { $response.OutputStream.Write($bytes,0,$bytes.Length); $response.Close() } catch {}
                }
            } finally { $listener.Stop(); $listener.Close() }
            """;
    }
}
