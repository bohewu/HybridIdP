#:project ../Infrastructure/Infrastructure.csproj

using Infrastructure.Directory;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

const string Prefix = "HYBRIDIDP_DIRECTORY_PROBE_";

string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(Prefix + name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing probe setting: {Prefix}{name}");
    }

    return value;
}

try
{
    var host = Required("HOST");
    var baseDistinguishedName = Required("BASE_DN");
    var managedSearchFilter = Required("MANAGED_FILTER");
    var account = Required("ACCOUNT");
    var port = int.TryParse(Environment.GetEnvironmentVariable(Prefix + "PORT"), out var configuredPort)
        ? configuredPort
        : 389;

    var transport = new LdapProtectedDirectoryTransport(
        Options.Create(new DirectoryCredentialTransportOptions
        {
            Host = host,
            Port = port,
            BaseDistinguishedName = baseDistinguishedName,
            ManagedSearchFilter = managedSearchFilter,
            WindowsDomain = Environment.GetEnvironmentVariable(Prefix + "WINDOWS_DOMAIN"),
            ServiceAccountUserName = Environment.GetEnvironmentVariable(Prefix + "SERVICE_ACCOUNT_USER_NAME"),
            ServiceAccountSecret = Environment.GetEnvironmentVariable(Prefix + "SERVICE_ACCOUNT_SECRET"),
            Timeout = TimeSpan.FromSeconds(5)
        }));

    var result = await transport.FindExactAsync(account, DirectoryTransport.WindowsNegotiate);
    var passed = result.Outcome == ProtectedDirectoryTransportOutcome.Succeeded &&
                 result.Identities.Count == 0;
    Console.WriteLine(
        $"Directory read-only probe: outcome={result.Outcome}; " +
        $"matches={result.Identities.Count}; status={(passed ? "PASS" : "FAIL")}");
    return passed ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Directory read-only probe failed: {exception.GetType().Name}");
    return 2;
}
