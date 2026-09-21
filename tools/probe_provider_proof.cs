#:project ../Infrastructure/Infrastructure.csproj

using Core.Application.DTOs;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Options;

const string Prefix = "HYBRIDIDP_PROVIDER_PROOF_PROBE_";

string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(Prefix + name);
    if (string.IsNullOrEmpty(value))
    {
        throw new InvalidOperationException($"Missing probe setting: {Prefix}{name}");
    }

    return value;
}

try
{
    var endpoint = Required("ENDPOINT");
    var sharedSecret = Required("SHARED_SECRET");
    var account = Required("ACCOUNT");
    var password = Required("PASSWORD");
    var expectedText = Required("EXPECTED_OUTCOME");

    if (!Enum.TryParse<ProofOutcome>(expectedText, ignoreCase: true, out var expected))
    {
        throw new InvalidOperationException("Expected outcome is not a supported proof outcome.");
    }

    using var httpClient = new HttpClient();
    var provider = new ProviderProofProvider(
        httpClient,
        Options.Create(new ProviderProofOptions
        {
            Endpoint = endpoint,
            SharedSecret = sharedSecret,
            AllowPrivateNetworkHttp = true,
            Timeout = TimeSpan.FromSeconds(5)
        }));

    var result = await provider.ProveAsync(
        new ProofRequest { AccountName = account },
        password);
    var contractValid = result.TryValidate(out _);
    var passed = contractValid && result.Outcome == expected;

    Console.WriteLine(
        $"Provider proof probe: expected={expected}; actual={result.Outcome}; " +
        $"contract={(contractValid ? "valid" : "invalid")}; status={(passed ? "PASS" : "FAIL")}");
    return passed ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"provider probe failed: {exception.GetType().Name}");
    return 2;
}
