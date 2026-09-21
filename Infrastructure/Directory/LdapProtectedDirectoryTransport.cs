using System.DirectoryServices.Protocols;
using System.Net;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Directory;

/// <summary>
/// LDAP implementation for the approved LDAPS, StartTLS, and Windows Negotiate transports.
/// It opens a fresh connection for every operation, including the post-reset verification bind.
/// </summary>
public sealed class LdapProtectedDirectoryTransport :
    IProtectedDirectoryIdentityTransport,
    IProtectedDirectoryCredentialTransport
{
    private static readonly string[] IdentityAttributes =
    [
        "objectGUID", "sAMAccountName", "userAccountControl", "lockoutTime",
        "displayName", "givenName", "sn", "mail", "department", "title", "employeeID"
    ];

    private readonly DirectoryCredentialTransportOptions _connectionOptions;

    public LdapProtectedDirectoryTransport(IOptions<DirectoryCredentialTransportOptions> connectionOptions)
    {
        _connectionOptions = connectionOptions.Value;
    }

    public async Task<ProtectedDirectoryTransportResult> FindExactAsync(
        string canonicalAccount,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(canonicalAccount))
        {
            return new ProtectedDirectoryTransportResult(ProtectedDirectoryTransportOutcome.Malformed, []);
        }

        try
        {
            using var connection = await OpenServiceConnectionAsync(transport, cancellationToken);
            var request = new SearchRequest(
                _connectionOptions.BaseDistinguishedName!,
                $"(&(objectClass=user)(sAMAccountName={EscapeFilterValue(canonicalAccount)}){_connectionOptions.ManagedSearchFilter})",
                SearchScope.Subtree,
                IdentityAttributes);
            var response = (SearchResponse)await SendAsync(connection, request, cancellationToken);
            var identities = response.Entries.Cast<SearchResultEntry>()
                .Select(ToIdentity)
                .Where(identity => identity is not null)
                .Cast<ManagedDirectoryIdentity>()
                .ToArray();
            return new ProtectedDirectoryTransportResult(ProtectedDirectoryTransportOutcome.Succeeded, identities);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LdapException exception) when (IsTimeout(exception))
        {
            return new ProtectedDirectoryTransportResult(ProtectedDirectoryTransportOutcome.Timeout, []);
        }
        catch (LdapException)
        {
            return ProtectedDirectoryTransportResult.Unavailable();
        }
        catch (DirectoryOperationException)
        {
            return ProtectedDirectoryTransportResult.Unavailable();
        }
        catch (InvalidOperationException)
        {
            return new ProtectedDirectoryTransportResult(ProtectedDirectoryTransportOutcome.Malformed, []);
        }
    }

    public Task<ProtectedDirectoryCredentialTransportResult> AuthenticateAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        BindExistingIdentityAsync(directoryObjectId, password, transport, includeIdentity: true, cancellationToken);

    public async Task<ProtectedDirectoryCredentialOperationTransportResult> ResetAsync(
        Guid directoryObjectId,
        string newPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (directoryObjectId == Guid.Empty || string.IsNullOrEmpty(newPassword))
        {
            return new ProtectedDirectoryCredentialOperationTransportResult(ProtectedDirectoryCredentialOperationTransportOutcome.Malformed);
        }

        try
        {
            using var connection = await OpenServiceConnectionAsync(transport, cancellationToken);
            var entry = await FindByObjectIdAsync(connection, directoryObjectId, cancellationToken);
            var identity = ToIdentity(entry);
            var gate = ClassifyIdentity(identity);
            if (gate is not null)
            {
                return new ProtectedDirectoryCredentialOperationTransportResult(MapOperation(gate.Value));
            }

            var quotedPassword = $"\"{newPassword}\"";
            var request = new ModifyRequest(
                entry.DistinguishedName,
                DirectoryAttributeOperation.Replace,
                "unicodePwd",
                System.Text.Encoding.Unicode.GetBytes(quotedPassword));
            await SendAsync(connection, request, cancellationToken);
            return new ProtectedDirectoryCredentialOperationTransportResult(ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LdapException exception) when (IsTimeout(exception))
        {
            return new ProtectedDirectoryCredentialOperationTransportResult(ProtectedDirectoryCredentialOperationTransportOutcome.Timeout);
        }
        catch (DirectoryOperationException)
        {
            return new ProtectedDirectoryCredentialOperationTransportResult(ProtectedDirectoryCredentialOperationTransportOutcome.Unavailable);
        }
        catch (LdapException)
        {
            return new ProtectedDirectoryCredentialOperationTransportResult(ProtectedDirectoryCredentialOperationTransportOutcome.Unavailable);
        }
        catch (InvalidOperationException)
        {
            return new ProtectedDirectoryCredentialOperationTransportResult(ProtectedDirectoryCredentialOperationTransportOutcome.Malformed);
        }
    }

    public async Task<ProtectedDirectoryCredentialTransportResult> VerifyAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        await BindExistingIdentityAsync(directoryObjectId, password, transport, includeIdentity: true, cancellationToken);

    public Task<ProtectedDirectoryCredentialOperationTransportResult> IssueTemporaryAsync(
        Guid directoryObjectId,
        string temporaryPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        ModifyCredentialAsync(
            directoryObjectId,
            transport,
            entry =>
            {
                var userAccountControl = AttributeInt64(entry, "userAccountControl") ?? 0;
                return (userAccountControl & 0x10000) != 0
                    ? null
                    : CreateTemporaryCredentialRequest(directoryObjectId, temporaryPassword);
            },
            cancellationToken);

    public Task<ProtectedDirectoryCredentialOperationTransportResult> ChangeRequiredAsync(
        Guid directoryObjectId,
        string currentPassword,
        string newPassword,
        DirectoryTransport transport,
        CancellationToken cancellationToken = default) =>
        ModifyCredentialAsync(
            directoryObjectId,
            transport,
            entry => CreateRequiredCredentialChangeRequest(
                directoryObjectId,
                currentPassword,
                newPassword),
            cancellationToken);

    private async Task<ProtectedDirectoryCredentialOperationTransportResult> ModifyCredentialAsync(
        Guid directoryObjectId,
        DirectoryTransport transport,
        Func<SearchResultEntry, ModifyRequest?> createRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (directoryObjectId == Guid.Empty)
        {
            return new(ProtectedDirectoryCredentialOperationTransportOutcome.Malformed);
        }

        try
        {
            using var connection = await OpenServiceConnectionAsync(transport, cancellationToken);
            var entry = await FindByObjectIdAsync(connection, directoryObjectId, cancellationToken);
            var gate = ClassifyIdentity(ToIdentity(entry));
            if (gate is not null)
            {
                return new(MapOperation(gate.Value));
            }

            var request = createRequest(entry);
            if (request is null)
            {
                return new(ProtectedDirectoryCredentialOperationTransportOutcome.Unsupported);
            }

            await SendAsync(connection, request, cancellationToken);
            return new(ProtectedDirectoryCredentialOperationTransportOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LdapException exception) when (IsTimeout(exception))
        {
            return new(ProtectedDirectoryCredentialOperationTransportOutcome.Timeout);
        }
        catch (DirectoryOperationException exception)
        {
            return new(exception.Response is ModifyResponse { ResultCode: not ResultCode.Success }
                ? ProtectedDirectoryCredentialOperationTransportOutcome.Rejected
                : ProtectedDirectoryCredentialOperationTransportOutcome.Unavailable);
        }
        catch (LdapException)
        {
            return new(ProtectedDirectoryCredentialOperationTransportOutcome.Unavailable);
        }
        catch (InvalidOperationException)
        {
            return new(ProtectedDirectoryCredentialOperationTransportOutcome.Malformed);
        }
    }

    internal static ModifyRequest CreateTemporaryCredentialRequest(Guid directoryObjectId, string temporaryPassword)
    {
        if (directoryObjectId == Guid.Empty || string.IsNullOrEmpty(temporaryPassword))
        {
            throw new InvalidOperationException("A directory identity and temporary password are required.");
        }

        var request = new ModifyRequest($"<GUID={directoryObjectId:D}>");
        request.Modifications.Add(PasswordModification(
            DirectoryAttributeOperation.Replace,
            temporaryPassword));
        var requireChange = new DirectoryAttributeModification
        {
            Name = "pwdLastSet",
            Operation = DirectoryAttributeOperation.Replace
        };
        requireChange.Add("0");
        request.Modifications.Add(requireChange);
        return request;
    }

    internal static ModifyRequest CreateRequiredCredentialChangeRequest(
        Guid directoryObjectId,
        string currentPassword,
        string newPassword)
    {
        if (directoryObjectId == Guid.Empty ||
            string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            throw new InvalidOperationException("A directory identity and both password values are required.");
        }

        var request = new ModifyRequest($"<GUID={directoryObjectId:D}>");
        request.Modifications.Add(PasswordModification(DirectoryAttributeOperation.Delete, currentPassword));
        request.Modifications.Add(PasswordModification(DirectoryAttributeOperation.Add, newPassword));
        return request;
    }

    private static DirectoryAttributeModification PasswordModification(
        DirectoryAttributeOperation operation,
        string password)
    {
        var modification = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = operation
        };
        modification.Add(System.Text.Encoding.Unicode.GetBytes($"\"{password}\""));
        return modification;
    }

    private async Task<ProtectedDirectoryCredentialTransportResult> BindExistingIdentityAsync(
        Guid directoryObjectId,
        string password,
        DirectoryTransport transport,
        bool includeIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (directoryObjectId == Guid.Empty || string.IsNullOrEmpty(password))
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Malformed);
        }

        try
        {
            SearchResultEntry entry;
            ManagedDirectoryIdentity? identity;
            using (var serviceConnection = await OpenServiceConnectionAsync(transport, cancellationToken))
            {
                entry = await FindByObjectIdAsync(serviceConnection, directoryObjectId, cancellationToken);
                identity = ToIdentity(entry);
            }

            var gate = ClassifyIdentity(identity);
            if (gate is not null)
            {
                return new ProtectedDirectoryCredentialTransportResult(gate.Value);
            }

            using (var credentialConnection = await OpenCredentialConnectionAsync(
                transport,
                entry.DistinguishedName,
                AttributeString(entry, "sAMAccountName") ?? entry.DistinguishedName,
                password,
                cancellationToken))
            {
                await BindAsync(credentialConnection, cancellationToken);
            }

            if (!includeIdentity)
            {
                return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Authenticated);
            }

            // A second privileged read verifies the immutable object and current status after the new-password bind.
            using var verificationConnection = await OpenServiceConnectionAsync(transport, cancellationToken);
            var verifiedIdentity = ToIdentity(await FindByObjectIdAsync(verificationConnection, directoryObjectId, cancellationToken));
            return new ProtectedDirectoryCredentialTransportResult(
                ClassifyIdentity(verifiedIdentity) ?? ProtectedDirectoryCredentialTransportOutcome.Authenticated,
                verifiedIdentity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LdapException exception) when (
            exception.ErrorCode == 49 && IsPasswordChangeRequired(exception.ServerErrorMessage))
        {
            return new ProtectedDirectoryCredentialTransportResult(
                ProtectedDirectoryCredentialTransportOutcome.PasswordChangeRequired);
        }
        catch (LdapException exception) when (exception.ErrorCode == 49)
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.InvalidCredentials);
        }
        catch (LdapException exception) when (IsTimeout(exception))
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Timeout);
        }
        catch (DirectoryOperationException)
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Unavailable);
        }
        catch (LdapException)
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Unavailable);
        }
        catch (InvalidOperationException)
        {
            return new ProtectedDirectoryCredentialTransportResult(ProtectedDirectoryCredentialTransportOutcome.Malformed);
        }
    }

    private async Task<LdapConnection> OpenServiceConnectionAsync(DirectoryTransport transport, CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(transport, cancellationToken);
        if (transport == DirectoryTransport.WindowsNegotiate)
        {
            connection.AuthType = AuthType.Negotiate;
            if (string.IsNullOrWhiteSpace(_connectionOptions.ServiceAccountUserName))
            {
                connection.Credential = CredentialCache.DefaultNetworkCredentials;
            }
            else
            {
                if (string.IsNullOrEmpty(_connectionOptions.ServiceAccountSecret))
                {
                    connection.Dispose();
                    throw new InvalidOperationException(
                        "A service-account secret is required when a Windows service account is configured.");
                }

                connection.Credential = string.IsNullOrWhiteSpace(_connectionOptions.WindowsDomain)
                    ? new NetworkCredential(
                        _connectionOptions.ServiceAccountUserName,
                        _connectionOptions.ServiceAccountSecret)
                    : new NetworkCredential(
                        _connectionOptions.ServiceAccountUserName,
                        _connectionOptions.ServiceAccountSecret,
                        _connectionOptions.WindowsDomain);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_connectionOptions.ServiceAccountDistinguishedName) ||
                string.IsNullOrEmpty(_connectionOptions.ServiceAccountSecret))
            {
                connection.Dispose();
                throw new InvalidOperationException("A protected service account is required for this directory transport.");
            }

            connection.AuthType = AuthType.Basic;
            connection.Credential = new NetworkCredential(
                _connectionOptions.ServiceAccountDistinguishedName,
                _connectionOptions.ServiceAccountSecret);
        }

        try
        {
            await BindAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private async Task<LdapConnection> OpenCredentialConnectionAsync(
        DirectoryTransport transport,
        string distinguishedName,
        string accountName,
        string password,
        CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(transport, cancellationToken);
        if (transport == DirectoryTransport.WindowsNegotiate)
        {
            connection.AuthType = AuthType.Negotiate;
            connection.Credential = string.IsNullOrWhiteSpace(_connectionOptions.WindowsDomain)
                ? new NetworkCredential(accountName, password)
                : new NetworkCredential(accountName, password, _connectionOptions.WindowsDomain);
        }
        else
        {
            connection.AuthType = AuthType.Basic;
            connection.Credential = new NetworkCredential(distinguishedName, password);
        }

        return connection;
    }

    private async Task<LdapConnection> OpenConnectionAsync(
        DirectoryTransport transport,
        CancellationToken cancellationToken)
    {
        if (!_connectionOptions.IsConfigured ||
            transport is not (DirectoryTransport.Ldaps or DirectoryTransport.StartTls or DirectoryTransport.WindowsNegotiate))
        {
            throw new InvalidOperationException("Directory transport is not configured for a protected operation.");
        }

        var port = _connectionOptions.Port ?? (transport == DirectoryTransport.Ldaps ? 636 : 389);
        var connection = new LdapConnection(new LdapDirectoryIdentifier(_connectionOptions.Host!, port))
        {
            Timeout = _connectionOptions.Timeout
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (transport == DirectoryTransport.Ldaps)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else if (transport == DirectoryTransport.StartTls)
        {
            try
            {
                using var registration = cancellationToken.Register(static state => ((LdapConnection)state!).Dispose(), connection);
                await Task.Run(() => connection.SessionOptions.StartTransportLayerSecurity(null), CancellationToken.None)
                    .WaitAsync(cancellationToken);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        return connection;
    }

    private async Task<SearchResultEntry> FindByObjectIdAsync(
        LdapConnection connection,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        var response = (SearchResponse)await SendAsync(connection, new SearchRequest(
            _connectionOptions.BaseDistinguishedName!,
            $"(&(objectClass=user)(objectGUID={EscapeGuid(objectId)}){_connectionOptions.ManagedSearchFilter})",
            SearchScope.Subtree,
            IdentityAttributes), cancellationToken);
        if (response.Entries.Count != 1)
        {
            throw new InvalidOperationException("The managed directory object was not uniquely resolved.");
        }

        return response.Entries[0];
    }

    // Protocols exposes synchronous bind/request APIs. Disposing the operation-local connection
    // is the LDAP cancellation path; WaitAsync returns promptly while the disposed operation unwinds.
    private static async Task BindAsync(LdapConnection connection, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static state => ((LdapConnection)state!).Dispose(), connection);
        await Task.Run(connection.Bind, CancellationToken.None).WaitAsync(cancellationToken);
    }

    private static async Task<DirectoryResponse> SendAsync(
        LdapConnection connection,
        DirectoryRequest request,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static state => ((LdapConnection)state!).Dispose(), connection);
        return await Task.Run(() => connection.SendRequest(request), CancellationToken.None)
            .WaitAsync(cancellationToken);
    }

    private static ManagedDirectoryIdentity? ToIdentity(SearchResultEntry entry)
    {
        var objectGuid = AttributeBytes(entry, "objectGUID");
        var account = AttributeString(entry, "sAMAccountName");
        if (objectGuid is null || objectGuid.Length != 16 || string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var userAccountControl = AttributeInt64(entry, "userAccountControl") ?? 0;
        var lockoutTime = AttributeInt64(entry, "lockoutTime") ?? 0;
        var profile = new AssuredProfile
        {
            DisplayName = AttributeString(entry, "displayName"),
            GivenName = AttributeString(entry, "givenName"),
            Surname = AttributeString(entry, "sn"),
            Email = AttributeString(entry, "mail"),
            Department = AttributeString(entry, "department"),
            Title = AttributeString(entry, "title"),
            EmployeeId = AttributeString(entry, "employeeID"),
            AssuredFields = ExistingProfileFields(entry)
        };
        return new ManagedDirectoryIdentity(
            new Guid(objectGuid),
            account,
            IsEligible: true,
            IsEnabled: (userAccountControl & 0x2) == 0,
            IsLocked: lockoutTime != 0,
            profile);
    }

    private static IReadOnlyList<AssuredProfileField> ExistingProfileFields(SearchResultEntry entry)
    {
        var fields = new List<AssuredProfileField>();
        AddIfPresent(entry, "displayName", AssuredProfileField.DisplayName, fields);
        AddIfPresent(entry, "givenName", AssuredProfileField.GivenName, fields);
        AddIfPresent(entry, "sn", AssuredProfileField.Surname, fields);
        AddIfPresent(entry, "mail", AssuredProfileField.Email, fields);
        AddIfPresent(entry, "department", AssuredProfileField.Department, fields);
        AddIfPresent(entry, "title", AssuredProfileField.Title, fields);
        AddIfPresent(entry, "employeeID", AssuredProfileField.EmployeeId, fields);
        return fields;
    }

    private static void AddIfPresent(SearchResultEntry entry, string attribute, AssuredProfileField field, ICollection<AssuredProfileField> fields)
    {
        if (!string.IsNullOrWhiteSpace(AttributeString(entry, attribute)))
        {
            fields.Add(field);
        }
    }

    private static ProtectedDirectoryCredentialTransportOutcome? ClassifyIdentity(ManagedDirectoryIdentity? identity) =>
        identity switch
        {
            null => ProtectedDirectoryCredentialTransportOutcome.Malformed,
            { IsEligible: false } => ProtectedDirectoryCredentialTransportOutcome.Ineligible,
            { IsEnabled: false } => ProtectedDirectoryCredentialTransportOutcome.Disabled,
            { IsLocked: true } => ProtectedDirectoryCredentialTransportOutcome.Locked,
            _ => null
        };

    private static ProtectedDirectoryCredentialOperationTransportOutcome MapOperation(ProtectedDirectoryCredentialTransportOutcome outcome) => outcome switch
    {
        ProtectedDirectoryCredentialTransportOutcome.Disabled => ProtectedDirectoryCredentialOperationTransportOutcome.Disabled,
        ProtectedDirectoryCredentialTransportOutcome.Locked => ProtectedDirectoryCredentialOperationTransportOutcome.Locked,
        ProtectedDirectoryCredentialTransportOutcome.Ineligible => ProtectedDirectoryCredentialOperationTransportOutcome.Ineligible,
        _ => ProtectedDirectoryCredentialOperationTransportOutcome.Malformed
    };

    private static string? AttributeString(SearchResultEntry entry, string name) =>
        entry.Attributes.Contains(name) && entry.Attributes[name].Count > 0
            ? entry.Attributes[name][0]?.ToString()
            : null;

    private static byte[]? AttributeBytes(SearchResultEntry entry, string name) =>
        entry.Attributes.Contains(name) && entry.Attributes[name].Count > 0
            ? entry.Attributes[name][0] as byte[]
            : null;

    private static long? AttributeInt64(SearchResultEntry entry, string name) =>
        long.TryParse(AttributeString(entry, name), out var value) ? value : null;

    private static bool IsTimeout(LdapException exception) => exception.ErrorCode is 85 or 1460;

    internal static bool IsPasswordChangeRequired(string? serverErrorMessage) =>
        !string.IsNullOrWhiteSpace(serverErrorMessage) &&
        System.Text.RegularExpressions.Regex.IsMatch(
            serverErrorMessage,
            @"(?:^|[\s,])data\s+773(?:[\s,]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string EscapeGuid(Guid value) => string.Concat(value.ToByteArray().Select(byteValue => $"\\{byteValue:X2}"));

    private static string EscapeFilterValue(string value) => string.Concat(value.Select(character => character switch
    {
        '\\' => "\\5c",
        '*' => "\\2a",
        '(' => "\\28",
        ')' => "\\29",
        '\0' => "\\00",
        _ => character.ToString()
    }));
}
