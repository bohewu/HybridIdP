using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Directory;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.SystemTests;

public sealed partial class NativePasswordRecoveryRazorSystemTests
{
    private static readonly NativeRecoveryContext ConnectedRecoveryContext =
        new("connected-primary-context", "connected-primary-csrf");

    [Fact]
    public void ConnectedReceiptStore_PersistsIntentAndImmutableAnchorWithoutCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hidp7-receipt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var receipt = new ConnectedReceiptStore(
                Path.Combine(root, "phase.json"),
                Path.Combine(root, "private-anchor.json"));
            receipt.WriteIntent("host.invalid", "OU=CodexSmoke,DC=example,DC=invalid", "smoke-cred-deadbeef");
            receipt.WritePhase("create-started", "started");
            receipt.WritePhase("create-acknowledged", "passed");
            receipt.WriteAnchor(
                "host.invalid",
                "OU=CodexSmoke,DC=example,DC=invalid",
                "OU=Quarantine,DC=example,DC=invalid",
                "smoke-cred-deadbeef",
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                "created",
                "creation-guid-readback");

            var phaseText = File.ReadAllText(receipt.PhasePath);
            var anchorText = File.ReadAllText(receipt.AnchorPath);
            Assert.Contains("create-started", phaseText, StringComparison.Ordinal);
            Assert.Contains("create-acknowledged", phaseText, StringComparison.Ordinal);
            Assert.Contains("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", anchorText, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", phaseText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", anchorText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConnectedReceiptStore_RejectsUnresolvedWriteAndAllowsRecordedKnownOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hidp7-write-barrier-{Guid.NewGuid():N}");
        const string account = "smoke-cred-deadbeef";
        var objectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        try
        {
            Directory.CreateDirectory(root);
            var receipts = new ConnectedReceiptStore(
                Path.Combine(root, "phase.json"),
                Path.Combine(root, "private-anchor.json"));
            receipts.WriteAnchor(
                "host.invalid",
                "OU=CodexSmoke,DC=example,DC=invalid",
                "OU=Quarantine,DC=example,DC=invalid",
                account,
                objectId,
                "created-guid-anchored",
                "creation-guid-readback");
            Assert.Equal(objectId, receipts.ReadCreatedAnchor(account).ObjectId);

            receipts.WriteUnresolvedWrite(
                "host.invalid",
                "OU=CodexSmoke,DC=example,DC=invalid",
                "OU=Quarantine,DC=example,DC=invalid",
                account,
                objectId);

            Assert.Contains(
                "write-outcome-unresolved",
                File.ReadAllText(receipts.AnchorPath),
                StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => receipts.ReadCreatedAnchor(account));
            Assert.Throws<InvalidOperationException>(() => receipts.ReadRetainedAnchor(account));

            receipts.WriteAnchor(
                "host.invalid",
                "OU=CodexSmoke,DC=example,DC=invalid",
                "OU=Quarantine,DC=example,DC=invalid",
                account,
                objectId,
                "disabled-quarantined-retained",
                "cleanup-disabled-quarantined-retained");
            Assert.Equal(objectId, receipts.ReadRetainedAnchor(account).ObjectId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "ExplicitConnectedE2E")]
    public void ConnectedPrimaryCeremonies_Preflight_ConfirmsTopologyAndFreshAccountAbsent()
    {
        if (!Enabled("RUN_HIDP7_CONNECTED_PRIMARY_PREFLIGHT")) return;

        var settings = ConnectedDirectorySettings.FromEnvironment();
        var receipts = new ConnectedReceiptStore(settings.PhaseReceiptPath, settings.PrivateAnchorPath);
        receipts.WriteIntent(settings.Host, settings.ActiveOuDn, settings.Account);
        using var connection = ConnectedDirectoryAccount.OpenServiceConnection(settings);
        ConnectedDirectoryAccount.ValidateTopology(connection, settings);
        Assert.False(
            ConnectedDirectoryAccount.AccountExists(connection, settings.ManagedRootDn, settings.Account),
            "The scoped fresh connected account already exists.");
        receipts.WritePhase("preflight-confirmed", "passed");
    }

    [Fact]
    [Trait("Category", "ExplicitConnectedE2E")]
    public async Task ConnectedPrimaryCeremonies_FreshDirectoryIdentity_FinalizesSecurityState()
    {
        if (!Enabled("RUN_HIDP7_CONNECTED_PRIMARY_CEREMONIES")) return;

        var settings = ConnectedDirectorySettings.FromEnvironment();
        var receipts = new ConnectedReceiptStore(settings.PhaseReceiptPath, settings.PrivateAnchorPath);
        receipts.RequirePreflightIntent(settings.Account, settings.ActiveOuDn);
        await using var directory = await ConnectedDirectoryAccount.CreateAsync(settings, receipts);
        await RunConnectedPrimaryCeremoniesAsync(directory, receipts);
    }

    [Fact]
    [Trait("Category", "ExplicitConnectedE2E")]
    public async Task ConnectedPrimaryCeremonies_HostStartup_ConfigIsValid()
    {
        if (!Enabled("RUN_HIDP7_CONNECTED_HOST_STARTUP")) return;

        await using var factory = await NativeRecoveryKestrelFactory.CreateConnectedDirectoryServicesAsync();
        Assert.NotNull(factory.Client.BaseAddress);
        await using var scope = factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ForgotPasswordRecoveryOptions>>().Value;
        Assert.Equal(ForgotPasswordMode.Native, options.DeploymentCeiling);
        Assert.True(options.NativeRecoveryEnabled);
        Assert.True(options.NativeDirectoryRecoveryEnabled);
        Assert.True(options.OrdinaryRecoveryAssistanceEnabled);
    }

    [Fact]
    [Trait("Category", "ExplicitConnectedE2E")]
    public async Task ConnectedPrimaryCeremonies_AnchoredContinuation_FinalizesSecurityState()
    {
        if (!Enabled("RUN_HIDP7_CONNECTED_PRIMARY_CONTINUATION")) return;

        var settings = ConnectedDirectorySettings.FromEnvironment();
        var receipts = new ConnectedReceiptStore(settings.PhaseReceiptPath, settings.PrivateAnchorPath);
        await using var directory = await ConnectedDirectoryAccount.AdoptDisabledAsync(settings, receipts);
        await RunConnectedPrimaryCeremoniesAsync(directory, receipts);
    }

    [Fact]
    [Trait("Category", "ExplicitConnectedE2E")]
    public async Task ConnectedPrimaryCeremonies_RetainedIdentity_FinalizesSecurityState()
    {
        if (!Enabled("RUN_HIDP7_CONNECTED_PRIMARY_RETAINED")) return;

        var settings = ConnectedDirectorySettings.FromEnvironment();
        var receipts = new ConnectedReceiptStore(settings.PhaseReceiptPath, settings.PrivateAnchorPath);
        await using var directory = await ConnectedDirectoryAccount.PrepareOperatorSettlementAsync(
            settings, receipts, ConnectedDirectoryAccount.GeneratePassword('P'));
        await RunConnectedPrimaryCeremoniesAsync(directory, receipts);
    }

    private static async Task RunConnectedPrimaryCeremoniesAsync(
        ConnectedDirectoryAccount directory,
        ConnectedReceiptStore receipts)
    {
        await using var factory = await NativeRecoveryKestrelFactory.CreateConnectedDirectoryServicesAsync();
        await SeedConnectedDirectoryAuthorityAsync(factory, directory);

        using var initialClient = factory.CreateBrowserClient();
        await SignInWithTotpAsync(initialClient, SyntheticIdentifier, directory.CurrentPassword, factory.TotpSecret, "/Account/Profile");
        _ = await IssueAuthorizationCodeTokensAsync(initialClient);
        var initialAuthentication = await CaptureActiveAuthenticationAsync(factory);

        var nativePassword = ConnectedDirectoryAccount.GeneratePassword('N');
        NativeRecoveryResetResult nativeResult;
        Guid nativeRequestId;
        receipts.WritePhase("native-reset-started", "started");
        directory.EnterUnknownWriteBarrier();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            nativeRequestId = await SeedNativeRecoveryApprovalAsync(scope.ServiceProvider, directory.ObjectId);
            nativeResult = await scope.ServiceProvider.GetRequiredService<INativePasswordRecoveryResetService>()
                .ResetAsync(new NativeRecoveryResetRequest(
                    nativeRequestId, string.Empty, nativePassword, ConnectedRecoveryContext,
                    UseAdministrativeApproval: true));
        }
        Assert.Equal(NativeRecoveryResetOutcome.Succeeded, nativeResult.Outcome);
        await VerifyExactAuthenticatedAsync(factory, directory, nativePassword);
        directory.SettleKnownWrite("native-reset-authenticated");
        await AssertConnectedNativeStateAsync(factory, directory.ObjectId, nativeRequestId);
        await AssertAuthenticationRevokedAsync(factory, initialAuthentication, "native-password-recovery");

        await using (var replayScope = factory.Services.CreateAsyncScope())
        {
            var db = replayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var countBefore = await db.NativeDirectoryRecoveryAttempts.CountAsync();
            var replay = await replayScope.ServiceProvider.GetRequiredService<INativePasswordRecoveryResetService>()
                .ResetAsync(new NativeRecoveryResetRequest(
                    nativeRequestId, string.Empty, ConnectedDirectoryAccount.GeneratePassword('X'),
                    ConnectedRecoveryContext, UseAdministrativeApproval: true));
            Assert.Equal(NativeRecoveryResetOutcome.Denied, replay.Outcome);
            Assert.Equal(countBefore, await db.NativeDirectoryRecoveryAttempts.CountAsync());
        }

        using var requiredClient = factory.CreateBrowserClient();
        await SignInWithTotpAsync(requiredClient, SyntheticIdentifier, nativePassword, factory.TotpSecret, "/Account/Profile");
        _ = await IssueAuthorizationCodeTokensAsync(requiredClient);
        var requiredAuthentication = await CaptureActiveAuthenticationAsync(factory);
        string stampBeforeRequiredChange;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == factory.UserId);
            stampBeforeRequiredChange = user.SecurityStamp!;
            user.RequiresPasswordChange = true;
            await db.SaveChangesAsync();
        }

        await directory.RequirePasswordChangeAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var precondition = await scope.ServiceProvider.GetRequiredService<IDirectoryCredentialVerifier>()
                .VerifyCredentialAsync(directory.ObjectId, nativePassword);
            Assert.Equal(DirectoryCredentialOutcome.PasswordChangeRequired, precondition.Outcome);
        }

        var requiredPassword = ConnectedDirectoryAccount.GeneratePassword('R');
        receipts.WritePhase("required-change-started", "started");
        directory.EnterUnknownWriteBarrier();
        RecoveryProofOutcome requiredResult;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            requiredResult = await scope.ServiceProvider.GetRequiredService<IDirectoryRequiredCredentialChangeService>()
                .ChangeAsync(new DirectoryRequiredCredentialChangeRequest(
                    factory.UserId, directory.ObjectId, nativePassword, requiredPassword));
        }
        Assert.Equal(RecoveryProofOutcome.Success, requiredResult);
        directory.SettleKnownWrite("required-change-acknowledged");
        await VerifyExactAuthenticatedAsync(factory, directory, requiredPassword);
        receipts.WritePhase("required-change-post-bind", "authenticated");
        await AssertConnectedRequiredChangeStateAsync(factory, directory.ObjectId, stampBeforeRequiredChange);
        await AssertAuthenticationRevokedAsync(factory, requiredAuthentication, "directory-required-credential-change");
        receipts.WritePhase("primary-ceremonies", "passed");
    }

    private static bool Enabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static async Task VerifyExactAuthenticatedAsync(
        NativeRecoveryKestrelFactory factory,
        ConnectedDirectoryAccount directory,
        string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var verification = await scope.ServiceProvider.GetRequiredService<IDirectoryCredentialVerifier>()
            .VerifyCredentialAsync(directory.ObjectId, password);
        Assert.True(
            verification.Outcome == DirectoryCredentialOutcome.Authenticated &&
            verification.Identity is { IsEnabled: true } identity &&
            identity.ObjectId == directory.ObjectId &&
            string.Equals(identity.CanonicalAccount, directory.Settings.Account, StringComparison.Ordinal),
            "Exact-GUID directory credential verification was not authenticated.");
    }

    private static async Task SeedConnectedDirectoryAuthorityAsync(
        NativeRecoveryKestrelFactory factory,
        ConnectedDirectoryAccount directory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == factory.UserId);
        var now = DateTimeOffset.UtcNow;
        var binding = new ProviderSubjectDirectoryBinding(
            user.Id, "connected-test", Guid.NewGuid().ToString("N"), directory.ObjectId,
            now.UtcDateTime, directory.Settings.Account);
        var migration = new CredentialMigrationStateRecord(user.Id, binding.Id, now);
        migration.Advance(CredentialMigrationState.ProofValidated, EffectiveEmailOtpRequirement.NotRequired, now);
        migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, now);
        migration.Advance(CredentialMigrationState.LocalFinalized, now);
        db.ProviderSubjectDirectoryBindings.Add(binding);
        db.CredentialMigrationStateRecords.Add(migration);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedNativeRecoveryApprovalAsync(IServiceProvider services, Guid directoryObjectId)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync();
        var recoveryEmail = await db.RecoveryEmails.SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var challenge = new RecoveryProofChallenge(
            recoveryEmail.Id, user.Id, RecoveryProofPurpose.NativePasswordRecovery,
            "connected-seeded-proof-state", now, now.AddMinutes(5));
        challenge.BindNativeAssistance(
            ConnectedRecoveryContext.ContextHash, ConnectedRecoveryContext.CsrfHash,
            directoryAuthority: true, directoryObjectId, recoveryEmail.Version, user.SecurityStamp!);
        var approval = new NativeRecoveryResetApproval(
            challenge.Id, user.Id, recoveryEmail.Id, recoveryEmail.Version, Guid.NewGuid(),
            ConnectedRecoveryContext.ContextHash, ConnectedRecoveryContext.CsrfHash,
            directoryAuthority: true, directoryObjectId, user.SecurityStamp!,
            "connected test approval", "authorized disposable identity", now, now.AddMinutes(5));
        db.RecoveryProofChallenges.Add(challenge);
        db.NativeRecoveryResetApprovals.Add(approval);
        await db.SaveChangesAsync();
        return challenge.Id;
    }

    private static async Task<PersistedAuthenticationState> CaptureActiveAuthenticationAsync(
        NativeRecoveryKestrelFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var valid = new List<object>();
        await foreach (var authorization in authorizationManager.FindBySubjectAsync(factory.UserId.ToString()))
        {
            if (string.Equals(await authorizationManager.GetStatusAsync(authorization), Statuses.Valid, StringComparison.Ordinal))
                valid.Add(authorization);
        }
        Assert.Single(valid);
        var authorizationId = await authorizationManager.GetIdAsync(valid[0]);
        Assert.False(string.IsNullOrWhiteSpace(authorizationId));
        var tokenIds = new List<string>();
        var tokenTypes = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var token in tokenManager.FindByAuthorizationIdAsync(authorizationId!))
        {
            if (!string.Equals(await tokenManager.GetStatusAsync(token), Statuses.Valid, StringComparison.Ordinal)) continue;
            tokenIds.Add((await tokenManager.GetIdAsync(token))!);
            tokenTypes.Add(await tokenManager.GetTypeAsync(token) ?? string.Empty);
        }
        Assert.Contains(TokenTypeIdentifiers.AccessToken, tokenTypes);
        Assert.Contains(TokenTypeIdentifiers.RefreshToken, tokenTypes);
        var session = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .UserSessions.AsNoTracking().SingleAsync(candidate => candidate.UserId == factory.UserId && candidate.RevokedUtc == null);
        return new PersistedAuthenticationState(authorizationId!, tokenIds, session.Id);
    }

    private static async Task AssertAuthenticationRevokedAsync(
        NativeRecoveryKestrelFactory factory,
        PersistedAuthenticationState state,
        string reason)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorization = await authorizationManager.FindByIdAsync(state.AuthorizationId);
        Assert.NotNull(authorization);
        Assert.Equal(Statuses.Revoked, await authorizationManager.GetStatusAsync(authorization!));
        foreach (var tokenId in state.TokenIds)
        {
            var token = await tokenManager.FindByIdAsync(tokenId);
            Assert.NotNull(token);
            Assert.Equal(Statuses.Revoked, await tokenManager.GetStatusAsync(token!));
        }
        var session = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .UserSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == state.SessionId);
        Assert.NotNull(session.RevokedUtc);
        Assert.Equal(reason, session.RevocationReason);
    }

    private static async Task AssertConnectedNativeStateAsync(
        NativeRecoveryKestrelFactory factory, Guid directoryObjectId, Guid requestId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == factory.UserId);
        var challenge = await db.RecoveryProofChallenges.AsNoTracking().SingleAsync(candidate => candidate.Id == requestId);
        var attempts = await db.NativeDirectoryRecoveryAttempts.AsNoTracking().ToListAsync();
        Assert.Equal(factory.OriginalPasswordHash, user.PasswordHash);
        Assert.NotEqual(factory.OriginalSecurityStamp, user.SecurityStamp);
        Assert.NotNull(challenge.ConsumedAtUtc);
        Assert.Single(attempts);
        Assert.Equal(directoryObjectId, attempts[0].DirectoryObjectId);
        Assert.Equal(NativeDirectoryCredentialOperationKind.NativeReset, attempts[0].OperationKind);
        Assert.Equal(NativeDirectoryRecoveryStatus.Succeeded, attempts[0].Status);
    }

    private static async Task AssertConnectedRequiredChangeStateAsync(
        NativeRecoveryKestrelFactory factory, Guid directoryObjectId, string priorStamp)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == factory.UserId);
        var attempts = await db.NativeDirectoryRecoveryAttempts.AsNoTracking().ToListAsync();
        Assert.Equal(factory.OriginalPasswordHash, user.PasswordHash);
        Assert.False(user.RequiresPasswordChange);
        Assert.NotEqual(priorStamp, user.SecurityStamp);
        Assert.Equal(2, attempts.Count(candidate =>
            candidate.DirectoryObjectId == directoryObjectId && candidate.Status == NativeDirectoryRecoveryStatus.Succeeded));
        Assert.Contains(attempts, candidate => candidate.OperationKind == NativeDirectoryCredentialOperationKind.RequiredChange);
    }

    private sealed record PersistedAuthenticationState(
        string AuthorizationId, IReadOnlyList<string> TokenIds, Guid SessionId);

    private sealed record ConnectedDirectorySettings(
        string Host, int Port, string BaseDn, string ManagedRootDn, string ActiveOuDn,
        string QuarantineOuDn, string WindowsDomain, string ServiceAccountUserName,
        string ServiceAccountSecret, string Account, string PhaseReceiptPath, string PrivateAnchorPath)
    {
        private const string Prefix = "HYBRIDIDP_CONNECTED_DIRECTORY_CREDENTIAL_SMOKE_";

        public static ConnectedDirectorySettings FromEnvironment()
        {
            var portText = Required("PORT");
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Connected directory configuration was rejected.");
            var account = Required("ACCOUNT");
            if (!Regex.IsMatch(account, "^smoke-cred-[a-z0-9]{8}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Connected directory account scope was rejected.");
            return new ConnectedDirectorySettings(
                Required("HOST"), port, Required("BASE_DN"), Required("MANAGED_ROOT_DN"),
                Required("ACTIVE_OU_DN"), Required("QUARANTINE_OU_DN"), Required("WINDOWS_DOMAIN"),
                Required("SERVICE_ACCOUNT_USER_NAME"), RequiredSecret("SERVICE_ACCOUNT_SECRET"), account,
                RequiredPath("PHASE_RECEIPT_PATH"), RequiredPath("PRIVATE_ANCHOR_PATH"));
        }

        private static string Required(string name) =>
            Environment.GetEnvironmentVariable(Prefix + name) is { Length: > 0 } value
                ? value.Trim()
                : throw new InvalidOperationException("Connected directory configuration was incomplete.");
        private static string RequiredSecret(string name) =>
            Environment.GetEnvironmentVariable(Prefix + name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException("Connected directory credential configuration was incomplete.");
        private static string RequiredPath(string name) => Path.GetFullPath(Required(name));
    }

    private sealed class ConnectedReceiptStore(string phasePath, string anchorPath)
    {
        public string PhasePath { get; } = Path.GetFullPath(phasePath);
        public string AnchorPath { get; } = Path.GetFullPath(anchorPath);

        public void WriteIntent(string host, string activeOuDn, string account) =>
            WriteDurable(AnchorPath, new { schema = 1, stage = "creation-intent", host, activeOuDn, account });

        public void WriteAnchor(
            string host, string activeOuDn, string quarantineOuDn, string account, Guid objectId,
            string stage, string knownOutcome) =>
            WriteDurable(AnchorPath, new
            {
                schema = 2,
                stage,
                knownOutcome,
                host,
                activeOuDn,
                quarantineOuDn,
                account,
                objectId
            });

        public void WriteUnresolvedWrite(
            string host, string activeOuDn, string quarantineOuDn, string account, Guid objectId) =>
            WriteDurable(AnchorPath, new
            {
                schema = 2,
                stage = "write-outcome-unresolved",
                host,
                activeOuDn,
                quarantineOuDn,
                account,
                objectId
            });

        public void WritePhase(string phase, string status) =>
            AppendDurablePhase(phase, status);

        public void RequirePreflightIntent(string account, string activeOuDn)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(AnchorPath));
            var root = document.RootElement;
            if (!string.Equals(root.GetProperty("stage").GetString(), "creation-intent", StringComparison.Ordinal) ||
                !string.Equals(root.GetProperty("account").GetString(), account, StringComparison.Ordinal) ||
                !string.Equals(root.GetProperty("activeOuDn").GetString(), activeOuDn, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Connected preflight intent did not match this exact operation.");
        }

        public ConnectedAnchor ReadCreatedAnchor(string account)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(AnchorPath));
            var root = document.RootElement;
            var stage = root.GetProperty("stage").GetString();
            var anchoredAccount = root.GetProperty("account").GetString();
            if (!string.Equals(stage, "created-guid-anchored", StringComparison.Ordinal) ||
                !string.Equals(anchoredAccount, account, StringComparison.Ordinal) ||
                !HasKnownOutcome(root) ||
                !Guid.TryParse(root.GetProperty("objectId").GetString(), out var objectId) ||
                objectId == Guid.Empty)
                throw new InvalidOperationException("Connected immutable identity anchor was invalid.");
            return new ConnectedAnchor(objectId);
        }

        public ConnectedAnchor ReadRetainedAnchor(string account)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(AnchorPath));
            var root = document.RootElement;
            var stage = root.GetProperty("stage").GetString();
            var anchoredAccount = root.GetProperty("account").GetString();
            if (!string.Equals(stage, "disabled-quarantined-retained", StringComparison.Ordinal) ||
                !string.Equals(anchoredAccount, account, StringComparison.Ordinal) ||
                !HasKnownOutcome(root) ||
                !Guid.TryParse(root.GetProperty("objectId").GetString(), out var objectId) ||
                objectId == Guid.Empty)
                throw new InvalidOperationException("Connected retained identity anchor was invalid.");
            return new ConnectedAnchor(objectId);
        }

        private static bool HasKnownOutcome(JsonElement root) =>
            root.TryGetProperty("knownOutcome", out var knownOutcome) &&
            knownOutcome.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(knownOutcome.GetString());

        private static void WriteDurable(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
            var temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }

        private void AppendDurablePhase(string phase, string status)
        {
            var history = new List<ConnectedPhaseReceipt>();
            if (File.Exists(PhasePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(PhasePath));
                if (document.RootElement.TryGetProperty("milestones", out var milestones))
                {
                    foreach (var milestone in milestones.EnumerateArray())
                    {
                        history.Add(milestone.Deserialize<ConnectedPhaseReceipt>()
                            ?? throw new InvalidOperationException("Connected phase receipt was malformed."));
                    }
                }
            }
            history.Add(new ConnectedPhaseReceipt(phase, status, DateTimeOffset.UtcNow));
            WriteDurable(PhasePath, new { schema = 1, milestones = history });
        }

        private sealed record ConnectedPhaseReceipt(string Phase, string Status, DateTimeOffset RecordedAtUtc);
        public sealed record ConnectedAnchor(Guid ObjectId);
    }

    private sealed class ConnectedDirectoryAccount : IAsyncDisposable
    {
        private const long DisabledAccountFlag = 0x2;
        private bool _cleanupAllowed;
        private string _knownAnchorStage;
        private readonly ConnectedReceiptStore _receipts;

        private ConnectedDirectoryAccount(
            ConnectedDirectorySettings settings, ConnectedReceiptStore receipts, Guid objectId,
            string currentPassword, string knownAnchorStage)
        {
            Settings = settings;
            _receipts = receipts;
            ObjectId = objectId;
            CurrentPassword = currentPassword;
            _knownAnchorStage = knownAnchorStage;
        }

        public ConnectedDirectorySettings Settings { get; }
        public Guid ObjectId { get; }
        public string CurrentPassword { get; }
        public bool CleanupAllowedForTesting => _cleanupAllowed;

        public static ConnectedDirectoryAccount CreateForReceiptTesting(
            ConnectedDirectorySettings settings,
            ConnectedReceiptStore receipts,
            bool cleanupAllowed) =>
            new(settings, receipts, Guid.NewGuid(), "not-used", "known")
            {
                _cleanupAllowed = cleanupAllowed
            };

        public static async Task<ConnectedDirectoryAccount> CreateAsync(
            ConnectedDirectorySettings settings, ConnectedReceiptStore receipts)
        {
            using var connection = OpenServiceConnection(settings);
            ValidateTopology(connection, settings);
            Require(!AccountExists(connection, settings.ManagedRootDn, settings.Account), "Fresh connected account already exists.");
            receipts.WritePhase("create-started", "started");
            var distinguishedName = $"CN={EscapeRdnValue(settings.Account)},{settings.ActiveOuDn}";
            connection.SendRequest(new AddRequest(
                distinguishedName,
                new DirectoryAttribute("objectClass", "top", "person", "organizationalPerson", "user"),
                new DirectoryAttribute("sAMAccountName", settings.Account),
                new DirectoryAttribute("displayName", settings.Account),
                new DirectoryAttribute("userAccountControl", "514")));
            receipts.WritePhase("create-acknowledged", "passed");
            var created = FindAccountAtDistinguishedName(connection, distinguishedName, settings.Account);
            Require(created.IsDisabled, "Created directory identity was not initially disabled.");
            Require(
                IsDirectChildByObjectId(connection, settings.ActiveOuDn, created.ObjectId, settings.Account),
                "Created directory identity was not a direct child of the exact active OU.");
            receipts.WriteAnchor(
                settings.Host, settings.ActiveOuDn, settings.QuarantineOuDn,
                settings.Account, created.ObjectId, "created-guid-anchored", "creation-guid-readback");
            var account = new ConnectedDirectoryAccount(
                settings, receipts, created.ObjectId, GeneratePassword('I'), "created-guid-anchored")
            {
                _cleanupAllowed = true
            };
            try
            {
                account.EnterUnknownWriteBarrier();
                SetPassword(connection, created.DistinguishedName, account.CurrentPassword);
                account.SettleKnownWrite("initial-password-acknowledged");
                account.EnterUnknownWriteBarrier();
                SetEnabled(connection, FindAccountByObjectId(connection, settings.ManagedRootDn, created.ObjectId, settings.Account));
                account.SettleKnownWrite("enable-acknowledged");
                var verification = await CreateTransport(settings).VerifyAsync(
                    created.ObjectId, account.CurrentPassword, DirectoryTransport.WindowsNegotiate);
                Require(
                    verification.Outcome == ProtectedDirectoryCredentialTransportOutcome.Authenticated &&
                    verification.Identity is { IsEnabled: true } identity && identity.ObjectId == created.ObjectId &&
                    string.Equals(identity.CanonicalAccount, settings.Account, StringComparison.Ordinal),
                    "Fresh connected account verification failed.");
                receipts.WritePhase("created-account-authenticated", "passed");
                return account;
            }
            catch
            {
                await account.DisposeAsync();
                throw;
            }
        }

        public static async Task<ConnectedDirectoryAccount> AdoptDisabledAsync(
            ConnectedDirectorySettings settings, ConnectedReceiptStore receipts)
        {
            var anchor = receipts.ReadCreatedAnchor(settings.Account);
            using var connection = OpenServiceConnection(settings);
            ValidateTopology(connection, settings);
            var existing = FindAccountByObjectId(
                connection, settings.ManagedRootDn, anchor.ObjectId, settings.Account);
            Require(existing.IsDisabled, "Anchored continuation identity was not disabled.");
            Require(
                IsDirectChildByObjectId(connection, settings.ActiveOuDn, anchor.ObjectId, settings.Account),
                "Anchored continuation identity was not in the expected direct CodexSmoke OU.");
            receipts.WritePhase("continuation-anchor-readonly-confirmed", "passed");
            var account = new ConnectedDirectoryAccount(
                settings, receipts, anchor.ObjectId, GeneratePassword('C'), "created-guid-anchored")
            {
                _cleanupAllowed = true
            };
            try
            {
                receipts.WritePhase("continuation-password-started", "started");
                account.EnterUnknownWriteBarrier();
                SetPassword(connection, existing.DistinguishedName, account.CurrentPassword);
                account.SettleKnownWrite("continuation-password-acknowledged");
                receipts.WritePhase("continuation-enable-started", "started");
                account.EnterUnknownWriteBarrier();
                SetEnabled(connection, FindAccountByObjectId(
                    connection, settings.ManagedRootDn, anchor.ObjectId, settings.Account));
                account.SettleKnownWrite("continuation-enable-acknowledged");
                var verification = await CreateTransport(settings).VerifyAsync(
                    anchor.ObjectId, account.CurrentPassword, DirectoryTransport.WindowsNegotiate);
                Require(
                    verification.Outcome == ProtectedDirectoryCredentialTransportOutcome.Authenticated &&
                    verification.Identity is { IsEnabled: true } identity && identity.ObjectId == anchor.ObjectId &&
                    string.Equals(identity.CanonicalAccount, settings.Account, StringComparison.Ordinal),
                    "Anchored continuation credential verification failed.");
                receipts.WritePhase("continuation-account-authenticated", "passed");
                return account;
            }
            catch
            {
                await account.DisposeAsync();
                throw;
            }
        }

        public static async Task<ConnectedDirectoryAccount> PrepareOperatorSettlementAsync(
            ConnectedDirectorySettings settings,
            ConnectedReceiptStore receipts,
            string currentPassword)
        {
            if (string.IsNullOrEmpty(currentPassword))
                throw new InvalidOperationException("Connected operator credential was unavailable.");
            var anchor = receipts.ReadRetainedAnchor(settings.Account);
            using var connection = OpenServiceConnection(settings);
            ValidateTopology(connection, settings);
            var existing = FindAccountByObjectId(
                connection, settings.ManagedRootDn, anchor.ObjectId, settings.Account);
            Require(existing.IsDisabled, "Anchored operator identity was not disabled.");
            Require(
                IsDirectChildByObjectId(connection, settings.QuarantineOuDn, anchor.ObjectId, settings.Account),
                "Anchored operator identity was not in the expected direct quarantine OU.");
            receipts.WritePhase("operator-anchor-readonly-confirmed", "passed");
            var account = new ConnectedDirectoryAccount(
                settings, receipts, anchor.ObjectId, currentPassword, "disabled-quarantined-retained")
            {
                _cleanupAllowed = true
            };
            try
            {
                receipts.WritePhase("operator-activate-move-started", "started");
                account.EnterUnknownWriteBarrier();
                MoveToOu(connection, existing.DistinguishedName, settings.ActiveOuDn);
                var activated = FindAccountByObjectId(
                    connection, settings.ManagedRootDn, anchor.ObjectId, settings.Account);
                Require(
                    IsDirectChildByObjectId(connection, settings.ActiveOuDn, anchor.ObjectId, settings.Account),
                    "Anchored operator identity was not moved to the exact CodexSmoke OU.");
                account.SettleKnownWrite("operator-activate-move-verified");
                receipts.WritePhase("operator-password-started", "started");
                account.EnterUnknownWriteBarrier();
                SetPassword(connection, activated.DistinguishedName, account.CurrentPassword);
                account.SettleKnownWrite("operator-password-acknowledged");
                receipts.WritePhase("operator-enable-started", "started");
                account.EnterUnknownWriteBarrier();
                SetEnabled(connection, FindAccountByObjectId(
                    connection, settings.ManagedRootDn, anchor.ObjectId, settings.Account));
                account.SettleKnownWrite("operator-enable-acknowledged");
                receipts.WriteAnchor(
                    settings.Host, settings.ActiveOuDn, settings.QuarantineOuDn,
                    settings.Account, anchor.ObjectId, "enabled-active-operator-prepared",
                    "operator-preparation-complete");
                account._knownAnchorStage = "enabled-active-operator-prepared";
                return account;
            }
            catch
            {
                await account.DisposeAsync();
                throw;
            }
        }

        public void EnterUnknownWriteBarrier()
        {
            _receipts.WriteUnresolvedWrite(
                Settings.Host,
                Settings.ActiveOuDn,
                Settings.QuarantineOuDn,
                Settings.Account,
                ObjectId);
            _cleanupAllowed = false;
        }

        public void SettleKnownWrite(string phase)
        {
            _receipts.WritePhase(phase, "passed");
            _receipts.WriteAnchor(
                Settings.Host,
                Settings.ActiveOuDn,
                Settings.QuarantineOuDn,
                Settings.Account,
                ObjectId,
                _knownAnchorStage,
                phase);
            _cleanupAllowed = true;
        }

        public Task RequirePasswordChangeAsync()
        {
            using var connection = OpenServiceConnection(Settings);
            var current = FindAccountByObjectId(connection, Settings.ManagedRootDn, ObjectId, Settings.Account);
            _receipts.WritePhase("require-password-change-started", "started");
            EnterUnknownWriteBarrier();
            connection.SendRequest(new ModifyRequest(
                current.DistinguishedName, DirectoryAttributeOperation.Replace, "pwdLastSet", "0"));
            SettleKnownWrite("require-password-change-acknowledged");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_cleanupAllowed) return ValueTask.CompletedTask;
            using var connection = OpenServiceConnection(Settings);
            var current = FindAccountByObjectId(connection, Settings.ManagedRootDn, ObjectId, Settings.Account);
            _receipts.WritePhase("cleanup-disable-started", "started");
            EnterUnknownWriteBarrier();
            SetDisabled(connection, current);
            var disabled = FindAccountByObjectId(connection, Settings.ManagedRootDn, ObjectId, Settings.Account);
            Require(disabled.IsDisabled, "Connected cleanup disable verification failed.");
            SettleKnownWrite("cleanup-disable-verified");
            if (!IsDirectChildByObjectId(connection, Settings.QuarantineOuDn, ObjectId, Settings.Account))
            {
                _receipts.WritePhase("cleanup-quarantine-started", "started");
                EnterUnknownWriteBarrier();
                MoveToOu(connection, disabled.DistinguishedName, Settings.QuarantineOuDn);
                var quarantined = FindAccountByObjectId(connection, Settings.ManagedRootDn, ObjectId, Settings.Account);
                Require(
                    quarantined.IsDisabled && IsDirectChildByObjectId(
                        connection, Settings.QuarantineOuDn, ObjectId, Settings.Account),
                    "Connected cleanup quarantine verification failed.");
                SettleKnownWrite("cleanup-quarantine-verified");
            }
            _receipts.WriteAnchor(
                Settings.Host, Settings.ActiveOuDn, Settings.QuarantineOuDn,
                Settings.Account, ObjectId, "disabled-quarantined-retained",
                "cleanup-disabled-quarantined-retained");
            _receipts.WritePhase("cleanup", "disabled-quarantined-retained");
            _cleanupAllowed = false;
            return ValueTask.CompletedTask;
        }

        public static string GeneratePassword(char marker) =>
            $"Aa1!{marker}{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}z#";

        private static LdapProtectedDirectoryTransport CreateTransport(ConnectedDirectorySettings settings) =>
            new(Options.Create(new DirectoryCredentialTransportOptions
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

        internal static LdapConnection OpenServiceConnection(ConnectedDirectorySettings settings)
        {
            var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, settings.Port))
            {
                AuthType = AuthType.Negotiate,
                Credential = settings.ServiceAccountUserName.Contains('\\', StringComparison.Ordinal) ||
                    settings.ServiceAccountUserName.Contains('@', StringComparison.Ordinal)
                    ? new NetworkCredential(settings.ServiceAccountUserName, settings.ServiceAccountSecret)
                    : new NetworkCredential(
                        settings.ServiceAccountUserName, settings.ServiceAccountSecret, settings.WindowsDomain),
                Timeout = TimeSpan.FromSeconds(5)
            };
            connection.SessionOptions.ProtocolVersion = 3;
            try { connection.Bind(); return connection; }
            catch { connection.Dispose(); throw; }
        }

        internal static void ValidateTopology(LdapConnection connection, ConnectedDirectorySettings settings)
        {
            var managedRoot = FindDirectoryObjectAtBase(connection, settings.ManagedRootDn);
            var activeOu = FindOrganizationalUnitAtBase(connection, settings.ActiveOuDn);
            var quarantineOu = FindOrganizationalUnitAtBase(connection, settings.QuarantineOuDn);
            Require(IsObjectInSubtree(connection, settings.BaseDn, managedRoot.ObjectId), "Directory topology was rejected.");
            Require(IsObjectInSubtree(connection, managedRoot.DistinguishedName, activeOu.ObjectId), "Directory topology was rejected.");
            Require(IsObjectInSubtree(connection, managedRoot.DistinguishedName, quarantineOu.ObjectId), "Directory topology was rejected.");
            Require(activeOu.ObjectId != quarantineOu.ObjectId, "Directory topology was rejected.");
            Require(string.Equals(
                activeOu.DistinguishedName.Split(',', 2)[0], "OU=CodexSmoke", StringComparison.OrdinalIgnoreCase),
                "The connected active OU was not the exact CodexSmoke OU.");
        }

        internal static bool AccountExists(LdapConnection connection, string baseDn, string account) =>
            Search(connection, baseDn,
                $"(&(objectClass=user)(sAMAccountName={EscapeFilterValue(account)}))",
                SearchScope.Subtree, ["objectGUID"]).Entries.Count != 0;

        private static DirectoryObject FindDirectoryObjectAtBase(LdapConnection connection, string distinguishedName)
        {
            var entry = ExactlyOne(Search(connection, distinguishedName, "(objectClass=*)", SearchScope.Base, ["objectGUID"]));
            return new DirectoryObject(ReadObjectId(entry), entry.DistinguishedName);
        }

        private static DirectoryObject FindOrganizationalUnitAtBase(LdapConnection connection, string distinguishedName)
        {
            var entry = ExactlyOne(Search(connection, distinguishedName, "(objectClass=organizationalUnit)", SearchScope.Base, ["objectGUID"]));
            return new DirectoryObject(ReadObjectId(entry), entry.DistinguishedName);
        }

        private static bool IsObjectInSubtree(LdapConnection connection, string baseDn, Guid objectId)
        {
            var response = Search(connection, baseDn, $"(objectGUID={EscapeGuid(objectId)})", SearchScope.Subtree, ["objectGUID"]);
            return response.Entries.Count == 1 && ReadObjectId(response.Entries[0]) == objectId;
        }

        private static DirectoryAccount FindAccountByName(LdapConnection connection, string baseDn, string account)
        {
            var entry = ExactlyOne(Search(connection, baseDn,
                $"(&(objectClass=user)(sAMAccountName={EscapeFilterValue(account)}))",
                SearchScope.Subtree, ["objectGUID", "sAMAccountName", "userAccountControl"]));
            return ReadAccount(entry, account);
        }

        private static DirectoryAccount FindAccountAtDistinguishedName(
            LdapConnection connection, string distinguishedName, string account)
        {
            var entry = ExactlyOne(Search(
                connection, distinguishedName, "(objectClass=user)", SearchScope.Base,
                ["objectGUID", "sAMAccountName", "userAccountControl"]));
            return ReadAccount(entry, account);
        }

        private static DirectoryAccount FindAccountByObjectId(
            LdapConnection connection, string baseDn, Guid objectId, string account)
        {
            var entry = ExactlyOne(Search(connection, baseDn,
                $"(&(objectClass=user)(objectGUID={EscapeGuid(objectId)}))",
                SearchScope.Subtree, ["objectGUID", "sAMAccountName", "userAccountControl"]));
            var resolved = ReadAccount(entry, account);
            Require(resolved.ObjectId == objectId, "Exact directory identity verification failed.");
            return resolved;
        }

        private static bool IsDirectChildByObjectId(
            LdapConnection connection, string parentDn, Guid objectId, string account)
        {
            var response = Search(connection, parentDn,
                $"(&(objectClass=user)(objectGUID={EscapeGuid(objectId)}))", SearchScope.OneLevel,
                ["objectGUID", "sAMAccountName", "userAccountControl"]);
            return response.Entries.Count == 1 && ReadAccount(response.Entries[0], account).ObjectId == objectId;
        }

        private static SearchResponse Search(
            LdapConnection connection, string baseDn, string filter, SearchScope scope, string[] attributes) =>
            (SearchResponse)connection.SendRequest(new SearchRequest(baseDn, filter, scope, attributes));

        private static SearchResultEntry ExactlyOne(SearchResponse response) => response.Entries.Count == 1
            ? response.Entries[0]
            : throw new InvalidOperationException("Exact directory search result was not unique.");

        private static DirectoryAccount ReadAccount(SearchResultEntry entry, string expectedAccount)
        {
            var canonical = AttributeString(entry, "sAMAccountName");
            Require(string.Equals(canonical, expectedAccount, StringComparison.Ordinal), "Exact directory account verification failed.");
            var control = AttributeInt64(entry, "userAccountControl")
                ?? throw new InvalidOperationException("Directory account state was unavailable.");
            return new DirectoryAccount(ReadObjectId(entry), entry.DistinguishedName, control, (control & DisabledAccountFlag) != 0);
        }

        private static Guid ReadObjectId(SearchResultEntry entry) =>
            entry.Attributes.Contains("objectGUID") && entry.Attributes["objectGUID"].Count == 1 &&
            entry.Attributes["objectGUID"][0] is byte[] bytes && bytes.Length == 16
                ? new Guid(bytes)
                : throw new InvalidOperationException("Directory object identity was unavailable.");

        private static string? AttributeString(SearchResultEntry entry, string name) =>
            entry.Attributes.Contains(name) && entry.Attributes[name].Count > 0
                ? entry.Attributes[name][0]?.ToString()
                : null;

        private static long? AttributeInt64(SearchResultEntry entry, string name) =>
            long.TryParse(AttributeString(entry, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : null;

        private static void SetPassword(LdapConnection connection, string distinguishedName, string password) =>
            connection.SendRequest(new ModifyRequest(
                distinguishedName, DirectoryAttributeOperation.Replace, "unicodePwd",
                Encoding.Unicode.GetBytes($"\"{password}\"")));

        private static void SetEnabled(LdapConnection connection, DirectoryAccount account) =>
            connection.SendRequest(new ModifyRequest(
                account.DistinguishedName, DirectoryAttributeOperation.Replace, "userAccountControl",
                (account.UserAccountControl & ~DisabledAccountFlag).ToString(CultureInfo.InvariantCulture)));

        private static void SetDisabled(LdapConnection connection, DirectoryAccount account) =>
            connection.SendRequest(new ModifyRequest(
                account.DistinguishedName, DirectoryAttributeOperation.Replace, "userAccountControl",
                (account.UserAccountControl | DisabledAccountFlag).ToString(CultureInfo.InvariantCulture)));

        private static void MoveToOu(LdapConnection connection, string distinguishedName, string parentDn) =>
            connection.SendRequest(new ModifyDNRequest(
                distinguishedName,
                parentDn,
                distinguishedName[..distinguishedName.IndexOf(',', StringComparison.Ordinal)]));

        private static string EscapeGuid(Guid value) =>
            string.Concat(value.ToByteArray().Select(byteValue => $"\\{byteValue:X2}"));

        private static string EscapeFilterValue(string value) => string.Concat(value.Select(character => character switch
        {
            '\\' => "\\5c", '*' => "\\2a", '(' => "\\28", ')' => "\\29", '\0' => "\\00", _ => character.ToString()
        }));

        private static string EscapeRdnValue(string value) => string.Concat(value.Select((character, index) => character switch
        {
            '\\' => "\\5c", ',' => "\\2c", '+' => "\\2b", '"' => "\\22", '<' => "\\3c", '>' => "\\3e",
            ';' => "\\3b", '=' => "\\3d", '#' when index == 0 => "\\23",
            ' ' when index == 0 || index == value.Length - 1 => "\\20", _ => character.ToString()
        }));

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed record DirectoryObject(Guid ObjectId, string DistinguishedName);
        private sealed record DirectoryAccount(
            Guid ObjectId, string DistinguishedName, long UserAccountControl, bool IsDisabled);
    }
}
