#:project ../Infrastructure/Infrastructure.csproj

using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Infrastructure.Directory;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

const string Prefix = "HYBRIDIDP_CONNECTED_DIRECTORY_CREDENTIAL_SMOKE_";
const string WriteConfirmation = "I_UNDERSTAND_THIS_WRITES_DIRECTORY_SMOKE_ACCOUNT";
const string SmokeAccountPattern = "^smoke-cred-[a-z0-9]{1,8}$";
const long DisabledAccountFlag = 0x2;

var receipt = new SmokeReceipt();
SmokeSettings? settings = null;
Guid? anchoredObjectId = null;
string? verifiedQuarantineOuDn = null;
var mutationStarted = false;
var exitCode = 1;

try
{
    receipt.Code = "WRITE_CONFIRMATION_REJECTED";
    if (!string.Equals(Environment.GetEnvironmentVariable(Prefix + "WRITE_CONFIRMATION"), WriteConfirmation, StringComparison.Ordinal))
    {
        throw new InvalidOperationException();
    }

    receipt.OptIn = true;
    receipt.Code = "CONFIGURATION_REJECTED";
    settings = ReadSettings();
    receipt.Configuration = true;

    DirectoryTopology topology;
    DirectoryAccount preflightAccount;
    using (var connection = OpenServiceConnection(settings))
    {
        receipt.Code = "TOPOLOGY_REJECTED";
        topology = ValidateTopology(connection, settings);
        verifiedQuarantineOuDn = topology.QuarantineOuDn;
        receipt.Topology = true;

        receipt.Code = "PREFLIGHT_REJECTED";
        preflightAccount = FindAccountByName(connection, settings.ManagedRootDn, settings.Account);
        Require(preflightAccount.IsDisabled);
        Require(IsDirectChildByObjectId(connection, topology.QuarantineOuDn, preflightAccount.ObjectId, settings.Account));
        anchoredObjectId = preflightAccount.ObjectId;
        receipt.Preflight = true;

        receipt.Code = "PREPARE_FAILED";
        var currentAccount = FindAccountByObjectId(connection, settings.ManagedRootDn, preflightAccount.ObjectId, settings.Account);
        Require(currentAccount.IsDisabled);
        Require(IsDirectChildByObjectId(connection, topology.QuarantineOuDn, currentAccount.ObjectId, settings.Account));

        var initialPassword = GeneratePassword('I');
        var replacementPassword = GeneratePassword('R');
        mutationStarted = true;
        SetPassword(connection, currentAccount.DistinguishedName, initialPassword);
        MoveToOu(connection, currentAccount.DistinguishedName, topology.ActiveOuDn);

        var activeAccount = FindAccountByObjectId(connection, settings.ManagedRootDn, preflightAccount.ObjectId, settings.Account);
        SetEnabled(connection, activeAccount);
        receipt.Prepared = true;

        var transport = new LdapProtectedDirectoryTransport(
            Options.Create(new DirectoryCredentialTransportOptions
            {
                Host = settings.Host,
                Port = settings.Port,
                BaseDistinguishedName = settings.BaseDn,
                ManagedSearchFilter = "(objectClass=user)",
                WindowsDomain = settings.WindowsDomain,
                ServiceAccountUserName = settings.ServiceAccountUserName,
                ServiceAccountSecret = settings.ServiceAccountSecret,
                Timeout = TimeSpan.FromSeconds(5)
            }));

        receipt.Code = "TRANSPORT_LOOKUP_FAILED";
        var lookup = await transport.FindExactAsync(settings.Account, DirectoryTransport.WindowsNegotiate);
        Require(
            lookup.Outcome == ProtectedDirectoryTransportOutcome.Succeeded &&
            lookup.Identities.Count == 1 &&
            lookup.Identities[0].ObjectId == preflightAccount.ObjectId &&
            string.Equals(lookup.Identities[0].CanonicalAccount, settings.Account, StringComparison.Ordinal) &&
            lookup.Identities[0].IsEnabled);
        receipt.TransportLookup = true;

        receipt.Code = "PASSWORD_RESET_FAILED";
        var reset = await transport.ResetAsync(
            preflightAccount.ObjectId,
            replacementPassword,
            DirectoryTransport.WindowsNegotiate);
        Require(reset.Outcome == ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded);
        receipt.PasswordReset = true;

        receipt.Code = "NEW_PASSWORD_VERIFICATION_FAILED";
        var newPasswordVerification = await transport.VerifyAsync(
            preflightAccount.ObjectId,
            replacementPassword,
            DirectoryTransport.WindowsNegotiate);
        Require(
            newPasswordVerification.Outcome == ProtectedDirectoryCredentialTransportOutcome.Authenticated &&
            newPasswordVerification.Identity is { } verifiedIdentity &&
            verifiedIdentity.ObjectId == preflightAccount.ObjectId &&
            string.Equals(verifiedIdentity.CanonicalAccount, settings.Account, StringComparison.Ordinal) &&
            verifiedIdentity.IsEnabled);
        receipt.NewPasswordVerified = true;

    }

    receipt.Code = "STAGE2_CREDENTIAL_SMOKE_SUCCEEDED";
    exitCode = 0;
}
catch
{
    exitCode = 1;
}
finally
{
    if (mutationStarted && settings is not null && anchoredObjectId is { } objectId && verifiedQuarantineOuDn is not null)
    {
        receipt.CleanupAttempted = true;
        try
        {
            receipt.CleanupVerified = CleanupToQuarantine(settings, objectId, verifiedQuarantineOuDn);
        }
        catch
        {
            receipt.CleanupVerified = false;
        }

        if (!receipt.CleanupVerified)
        {
            receipt.Code = "CLEANUP_UNCERTAIN";
            exitCode = 1;
        }
    }

    Console.WriteLine(receipt.ToSanitizedReceipt());
}

return exitCode;

static SmokeSettings ReadSettings()
{
    var portText = Required("PORT");
    if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException();
    }

    var account = Required("ACCOUNT");
    if (!Regex.IsMatch(account, SmokeAccountPattern, RegexOptions.CultureInvariant))
    {
        throw new InvalidOperationException();
    }

    return new SmokeSettings(
        Required("HOST"),
        port,
        Required("BASE_DN"),
        Required("MANAGED_ROOT_DN"),
        Required("ACTIVE_OU_DN"),
        Required("QUARANTINE_OU_DN"),
        Required("WINDOWS_DOMAIN"),
        Required("SERVICE_ACCOUNT_USER_NAME"),
        RequiredSecret("SERVICE_ACCOUNT_SECRET"),
        account);
}

static string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(Prefix + name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException();
    }

    return value.Trim();
}

static string RequiredSecret(string name)
{
    var value = Environment.GetEnvironmentVariable(Prefix + name);
    if (string.IsNullOrEmpty(value))
    {
        throw new InvalidOperationException();
    }

    return value;
}

static LdapConnection OpenServiceConnection(SmokeSettings settings)
{
    var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, settings.Port))
    {
        AuthType = AuthType.Negotiate,
        Credential = new NetworkCredential(
            settings.ServiceAccountUserName,
            settings.ServiceAccountSecret,
            settings.WindowsDomain),
        Timeout = TimeSpan.FromSeconds(5)
    };
    connection.SessionOptions.ProtocolVersion = 3;

    try
    {
        connection.Bind();
        return connection;
    }
    catch
    {
        connection.Dispose();
        throw;
    }
}

static DirectoryTopology ValidateTopology(LdapConnection connection, SmokeSettings settings)
{
    var managedRoot = FindDirectoryObjectAtBase(connection, settings.ManagedRootDn);
    var activeOu = FindOrganizationalUnitAtBase(connection, settings.ActiveOuDn);
    var quarantineOu = FindOrganizationalUnitAtBase(connection, settings.QuarantineOuDn);

    Require(IsObjectInSubtree(connection, settings.BaseDn, managedRoot.ObjectId));
    Require(managedRoot.ObjectId != activeOu.ObjectId);
    Require(managedRoot.ObjectId != quarantineOu.ObjectId);
    Require(activeOu.ObjectId != quarantineOu.ObjectId);
    Require(IsObjectInSubtree(connection, managedRoot.DistinguishedName, activeOu.ObjectId));
    Require(IsObjectInSubtree(connection, managedRoot.DistinguishedName, quarantineOu.ObjectId));

    return new DirectoryTopology(activeOu.DistinguishedName, quarantineOu.DistinguishedName);
}

static DirectoryObject FindDirectoryObjectAtBase(LdapConnection connection, string distinguishedName)
{
    var entry = ExactlyOne(Search(
        connection,
        distinguishedName,
        "(objectClass=*)",
        SearchScope.Base,
        ["objectGUID"]));
    return new DirectoryObject(ReadObjectId(entry), entry.DistinguishedName);
}

static DirectoryObject FindOrganizationalUnitAtBase(LdapConnection connection, string distinguishedName)
{
    var entry = ExactlyOne(Search(
        connection,
        distinguishedName,
        "(objectClass=organizationalUnit)",
        SearchScope.Base,
        ["objectGUID"]));
    return new DirectoryObject(ReadObjectId(entry), entry.DistinguishedName);
}

static bool IsObjectInSubtree(LdapConnection connection, string baseDn, Guid objectId)
{
    var response = Search(
        connection,
        baseDn,
        $"(objectGUID={EscapeGuid(objectId)})",
        SearchScope.Subtree,
        ["objectGUID"]);
    return response.Entries.Count == 1 && ReadObjectId(response.Entries[0]) == objectId;
}

static DirectoryAccount FindAccountByName(LdapConnection connection, string baseDn, string account)
{
    var entry = ExactlyOne(Search(
        connection,
        baseDn,
        $"(&(objectClass=user)(sAMAccountName={EscapeFilterValue(account)}))",
        SearchScope.Subtree,
        ["objectGUID", "sAMAccountName", "userAccountControl"]));
    return ReadAccount(entry, account);
}

static DirectoryAccount FindAccountByObjectId(LdapConnection connection, string baseDn, Guid objectId, string account)
{
    var entry = ExactlyOne(Search(
        connection,
        baseDn,
        $"(&(objectClass=user)(objectGUID={EscapeGuid(objectId)}))",
        SearchScope.Subtree,
        ["objectGUID", "sAMAccountName", "userAccountControl"]));
    var resolved = ReadAccount(entry, account);
    Require(resolved.ObjectId == objectId);
    return resolved;
}

static bool IsDirectChildByObjectId(LdapConnection connection, string parentDn, Guid objectId, string account)
{
    var response = Search(
        connection,
        parentDn,
        $"(&(objectClass=user)(objectGUID={EscapeGuid(objectId)}))",
        SearchScope.OneLevel,
        ["objectGUID", "sAMAccountName", "userAccountControl"]);
    if (response.Entries.Count != 1)
    {
        return false;
    }

    var resolved = ReadAccount(response.Entries[0], account);
    return resolved.ObjectId == objectId;
}

static SearchResponse Search(
    LdapConnection connection,
    string baseDn,
    string filter,
    SearchScope scope,
    string[] attributes) =>
    (SearchResponse)connection.SendRequest(new SearchRequest(baseDn, filter, scope, attributes));

static SearchResultEntry ExactlyOne(SearchResponse response)
{
    if (response.Entries.Count != 1)
    {
        throw new InvalidOperationException();
    }

    return response.Entries[0];
}

static DirectoryAccount ReadAccount(SearchResultEntry entry, string expectedAccount)
{
    var canonicalAccount = AttributeString(entry, "sAMAccountName");
    if (!string.Equals(canonicalAccount, expectedAccount, StringComparison.Ordinal))
    {
        throw new InvalidOperationException();
    }

    var userAccountControl = AttributeInt64(entry, "userAccountControl");
    if (userAccountControl is null)
    {
        throw new InvalidOperationException();
    }

    return new DirectoryAccount(
        ReadObjectId(entry),
        entry.DistinguishedName,
        userAccountControl.Value,
        (userAccountControl.Value & DisabledAccountFlag) != 0);
}

static Guid ReadObjectId(SearchResultEntry entry)
{
    var value = entry.Attributes.Contains("objectGUID") && entry.Attributes["objectGUID"].Count > 0
        ? entry.Attributes["objectGUID"][0] as byte[]
        : null;
    if (value is not { Length: 16 })
    {
        throw new InvalidOperationException();
    }

    var objectId = new Guid(value);
    if (objectId == Guid.Empty)
    {
        throw new InvalidOperationException();
    }

    return objectId;
}

static string? AttributeString(SearchResultEntry entry, string name) =>
    entry.Attributes.Contains(name) && entry.Attributes[name].Count > 0
        ? entry.Attributes[name][0]?.ToString()
        : null;

static long? AttributeInt64(SearchResultEntry entry, string name) =>
    long.TryParse(AttributeString(entry, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value
        : null;

static void SetPassword(LdapConnection connection, string distinguishedName, string password) =>
    connection.SendRequest(new ModifyRequest(
        distinguishedName,
        DirectoryAttributeOperation.Replace,
        "unicodePwd",
        Encoding.Unicode.GetBytes($"\"{password}\"")));

static void MoveToOu(LdapConnection connection, string distinguishedName, string targetOuDn) =>
    connection.SendRequest(new ModifyDNRequest(distinguishedName, targetOuDn, FirstRelativeDistinguishedName(distinguishedName))
    {
        DeleteOldRdn = true
    });

static void SetEnabled(LdapConnection connection, DirectoryAccount account) =>
    connection.SendRequest(new ModifyRequest(
        account.DistinguishedName,
        DirectoryAttributeOperation.Replace,
        "userAccountControl",
        (account.UserAccountControl & ~DisabledAccountFlag).ToString(CultureInfo.InvariantCulture)));

static void SetDisabled(LdapConnection connection, DirectoryAccount account) =>
    connection.SendRequest(new ModifyRequest(
        account.DistinguishedName,
        DirectoryAttributeOperation.Replace,
        "userAccountControl",
        (account.UserAccountControl | DisabledAccountFlag).ToString(CultureInfo.InvariantCulture)));

static bool CleanupToQuarantine(SmokeSettings settings, Guid objectId, string quarantineOuDn)
{
    using var connection = OpenServiceConnection(settings);
    var currentAccount = FindAccountByObjectId(connection, settings.ManagedRootDn, objectId, settings.Account);
    SetDisabled(connection, currentAccount);

    var disabledAccount = FindAccountByObjectId(connection, settings.ManagedRootDn, objectId, settings.Account);
    if (!disabledAccount.IsDisabled)
    {
        return false;
    }

    if (!IsDirectChildByObjectId(connection, quarantineOuDn, objectId, settings.Account))
    {
        MoveToOu(connection, disabledAccount.DistinguishedName, quarantineOuDn);
    }

    var finalAccount = FindAccountByObjectId(connection, settings.ManagedRootDn, objectId, settings.Account);
    return finalAccount.IsDisabled &&
           IsDirectChildByObjectId(connection, quarantineOuDn, objectId, settings.Account);
}

static string FirstRelativeDistinguishedName(string distinguishedName)
{
    var escaped = false;
    var quoted = false;
    for (var index = 0; index < distinguishedName.Length; index++)
    {
        var character = distinguishedName[index];
        if (escaped)
        {
            escaped = false;
            continue;
        }

        if (character == '\\')
        {
            escaped = true;
            continue;
        }

        if (character == '"')
        {
            quoted = !quoted;
            continue;
        }

        if (character == ',' && !quoted)
        {
            return distinguishedName[..index];
        }
    }

    throw new InvalidOperationException();
}

static string GeneratePassword(char marker) =>
    $"Aa1!{marker}{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}z#";

static string EscapeGuid(Guid value) =>
    string.Concat(value.ToByteArray().Select(byteValue => $"\\{byteValue:X2}"));

static string EscapeFilterValue(string value) =>
    string.Concat(value.Select(character => character switch
    {
        '\\' => "\\5c",
        '*' => "\\2a",
        '(' => "\\28",
        ')' => "\\29",
        '\0' => "\\00",
        _ => character.ToString()
    }));

static void Require(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException();
    }
}

sealed record SmokeSettings(
    string Host,
    int Port,
    string BaseDn,
    string ManagedRootDn,
    string ActiveOuDn,
    string QuarantineOuDn,
    string WindowsDomain,
    string ServiceAccountUserName,
    string ServiceAccountSecret,
    string Account);

sealed record DirectoryObject(Guid ObjectId, string DistinguishedName);

sealed record DirectoryTopology(string ActiveOuDn, string QuarantineOuDn);

sealed record DirectoryAccount(
    Guid ObjectId,
    string DistinguishedName,
    long UserAccountControl,
    bool IsDisabled);

sealed class SmokeReceipt
{
    public string Code { get; set; } = "NOT_STARTED";
    public bool OptIn { get; set; }
    public bool Configuration { get; set; }
    public bool Topology { get; set; }
    public bool Preflight { get; set; }
    public bool Prepared { get; set; }
    public bool TransportLookup { get; set; }
    public bool PasswordReset { get; set; }
    public bool NewPasswordVerified { get; set; }
    public bool CleanupAttempted { get; set; }
    public bool CleanupVerified { get; set; }

    public string ToSanitizedReceipt()
    {
        var status = CleanupAttempted && !CleanupVerified
            ? "ACTION_REQUIRED"
            : Code == "STAGE2_CREDENTIAL_SMOKE_SUCCEEDED"
                ? "PASS"
                : "FAIL";
        return $"stage2_directory_credential_smoke; code={Code}; opt_in={Value(OptIn)}; configuration={Value(Configuration)}; " +
               $"topology={Value(Topology)}; preflight={Value(Preflight)}; prepared={Value(Prepared)}; " +
               $"transport_lookup={Value(TransportLookup)}; password_reset={Value(PasswordReset)}; " +
               $"new_password_verified={Value(NewPasswordVerified)}; " +
               $"cleanup_attempted={Value(CleanupAttempted)}; cleanup_verified={Value(CleanupVerified)}; status={status}";
    }

    private static string Value(bool value) => value ? "true" : "false";
}
