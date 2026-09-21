using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.SystemTests;

public sealed partial class NativePasswordRecoveryRazorSystemTests
{
    [Fact]
    [Trait("Category", "ExplicitConnectedE2E")]
    public async Task PendingSettlement_ConnectedCodexSmoke_BrowserJourneyResolvesOnce()
    {
        if (!Enabled("RUN_HIDP7_CONNECTED_OPERATOR_SETTLEMENT")) return;

        var readyFile = RequireEnvironmentPath("HIDP7_T2_OPERATOR_READY_FILE");
        var stopFile = RequireEnvironmentPath("HIDP7_T2_OPERATOR_STOP_FILE");
        var assertionReceiptFile = RequireEnvironmentPath("HIDP7_T2_OPERATOR_ASSERTION_RECEIPT_FILE");
        var cleanupReceiptFile = RequireEnvironmentPath("HIDP7_T2_OPERATOR_CLEANUP_RECEIPT_FILE");
        var knownCleanupReceiptFile = RequireEnvironmentPath("HIDP7_T2_KNOWN_CLEANUP_RECEIPT_FILE");
        var currentCredential = Environment.GetEnvironmentVariable("HIDP7_T2_DIRECTORY_CURRENT_CREDENTIAL")
            ?? throw new InvalidOperationException("Connected operator credential was unavailable.");
        var settings = ConnectedDirectorySettings.FromEnvironment();
        RequireKnownRetainedState(knownCleanupReceiptFile, settings.PrivateAnchorPath);
        var receipts = new ConnectedReceiptStore(settings.PhaseReceiptPath, settings.PrivateAnchorPath);
        ConnectedDirectoryAccount? directory = null;
        NativeRecoveryKestrelFactory? factory = null;
        try
        {
            directory = await ConnectedDirectoryAccount.PrepareOperatorSettlementAsync(
                settings, receipts, currentCredential);
            currentCredential = string.Empty;
            factory = await NativeRecoveryKestrelFactory.CreateConnectedSettlementServicesAsync(
                directory.ObjectId, settings.Account);

            using (var anonymousPending = await factory.Client.GetAsync(
                       $"/api/admin/users/{factory.SettlementTargetId}/credential-recovery/pending-directory-operation"))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousPending.StatusCode);
            }

            WriteDurableJson(readyFile, new
            {
                baseUrl = factory.Client.BaseAddress!.AbsoluteUri.TrimEnd('/'),
                targetIdentifier = factory.SettlementTargetIdentifier,
                targetRecoveryAddress = factory.SettlementRecoveryAddress,
                syntheticPendingAttempt = true,
                ownedGuidAnchorVerified = true,
                knownStartState = true,
                oldUnknownUntouched = true
            });
            Assert.True(
                await WaitForFileAsync(stopFile, TimeSpan.FromMinutes(7)),
                "The bounded connected pending-settlement browser exercise did not signal completion in time.");

            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await dbContext.NativeDirectoryRecoveryAttempts.AsNoTracking()
                .SingleAsync(candidate => candidate.LocalAccountId == factory.SettlementTargetId);
            var preparation = await dbContext.DirectorySettlementPreparations.AsNoTracking().SingleAsync();
            var challenge = await dbContext.RecoveryProofChallenges.AsNoTracking()
                .SingleAsync(candidate => candidate.Purpose == RecoveryProofPurpose.PendingDirectorySettlement);
            var target = await dbContext.Users.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == factory.SettlementTargetId);
            var binding = await dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
                .SingleAsync(candidate => candidate.LocalAccountId == factory.SettlementTargetId);
            var session = await dbContext.UserSessions.AsNoTracking()
                .SingleAsync(candidate => candidate.UserId == factory.SettlementTargetId);
            var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var authorizations = new List<object>();
            await foreach (var authorization in authorizationManager.FindBySubjectAsync(
                               factory.SettlementTargetId.ToString()))
            {
                authorizations.Add(authorization);
            }
            var tokens = new List<object>();
            await foreach (var token in tokenManager.FindBySubjectAsync(factory.SettlementTargetId.ToString()))
            {
                tokens.Add(token);
            }

            var verifier = factory.ConnectedDirectoryVerifier;
            var exactBinding = binding.DirectoryObjectId == directory.ObjectId;
            if (verifier.Outcome != DirectoryCredentialOutcome.Authenticated)
            {
                var pendingAttemptRetained = attempt.Status == NativeDirectoryRecoveryStatus.Reserved;
                var localPasswordHashUnchanged = string.Equals(
                    target.PasswordHash, factory.SettlementOriginalPasswordHash, StringComparison.Ordinal);
                WriteDurableJson(assertionReceiptFile, new
                {
                    status = "observed-not-authenticated",
                    settlementOrigin = "synthetic-pending-attempt",
                    verifierCalls = verifier.VerifyCalls,
                    verifierOutcome = verifier.Outcome?.ToString() ?? "NotInvoked",
                    verifierElapsedMilliseconds = verifier.ElapsedMilliseconds,
                    requestedExactGuid = verifier.RequestedExactObjectId,
                    returnedExactGuid = verifier.ReturnedExactObjectId,
                    canonicalAccountMatched = verifier.CanonicalAccountMatched,
                    exactBinding,
                    attemptStatus = attempt.Status.ToString(),
                    pendingAttemptRetained,
                    preparationStatus = preparation.Status.ToString(),
                    localPasswordHashUnchanged,
                    oldUnknownUntouched = true,
                    secretsPersisted = false
                });
                Assert.True(pendingAttemptRetained, "The synthetic pending attempt barrier was not retained.");
                Assert.True(localPasswordHashUnchanged, "The local password hash changed after rejected verification.");
                Assert.Fail($"Connected verifier returned safe outcome {verifier.Outcome?.ToString() ?? "NotInvoked"}.");
            }

            Assert.Equal(1, verifier.VerifyCalls);
            Assert.True(verifier.RequestedExactObjectId);
            Assert.True(verifier.ReturnedExactObjectId);
            Assert.True(verifier.CanonicalAccountMatched);
            Assert.True(exactBinding);
            Assert.Equal(NativeDirectoryRecoveryStatus.OperatorResolved, attempt.Status);
            Assert.Equal(DirectorySettlementPreparationStatus.Consumed, preparation.Status);
            Assert.NotNull(challenge.ConsumedAtUtc);
            Assert.True(target.RequiresPasswordChange);
            Assert.Equal(factory.SettlementOriginalPasswordHash, target.PasswordHash);
            Assert.NotEqual(factory.SettlementOriginalSecurityStamp, target.SecurityStamp);
            Assert.NotNull(session.RevokedUtc);
            Assert.Equal("directory-credential-operator-resolution", session.RevocationReason);
            Assert.Single(authorizations);
            foreach (var authorization in authorizations)
            {
                Assert.Equal(Statuses.Revoked, await authorizationManager.GetStatusAsync(authorization));
            }
            Assert.Equal(2, tokens.Count);
            var tokenTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in tokens)
            {
                Assert.Equal(Statuses.Revoked, await tokenManager.GetStatusAsync(token));
                tokenTypes.Add(await tokenManager.GetTypeAsync(token) ?? string.Empty);
            }
            Assert.Contains(TokenTypeIdentifiers.AccessToken, tokenTypes);
            Assert.Contains(TokenTypeIdentifiers.RefreshToken, tokenTypes);
            WriteDurableJson(assertionReceiptFile, new
            {
                status = "passed",
                settlementOrigin = "synthetic-pending-attempt",
                realDirectoryVerification = "authenticated",
                verifierCalls = verifier.VerifyCalls,
                verifierElapsedMilliseconds = verifier.ElapsedMilliseconds,
                requestedExactGuid = verifier.RequestedExactObjectId,
                returnedExactGuid = verifier.ReturnedExactObjectId,
                canonicalAccountMatched = verifier.CanonicalAccountMatched,
                exactBinding,
                attempt = "operator-resolved",
                preparation = "consumed",
                ownershipChallenge = "consumed",
                requiresPasswordChange = true,
                localPasswordHash = "unchanged",
                securityStamp = "rotated",
                session = "revoked",
                authorizationCount = authorizations.Count,
                revokedAuthorizationCount = authorizations.Count,
                tokenCount = tokens.Count,
                revokedTokenCount = tokens.Count,
                tokenTypes = tokenTypes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                replayExpectedRejected = true,
                oldUnknownUntouched = true,
                secretsPersisted = false
            });
        }
        catch
        {
            if (!File.Exists(assertionReceiptFile))
            {
                WriteDurableJson(assertionReceiptFile, new
                {
                    status = "failed-before-assertions",
                    stage = File.Exists(readyFile) ? "browser-or-assertions" : "known-state-preparation",
                    oldUnknownUntouched = true,
                    secretsPersisted = false
                });
            }
            throw;
        }
        finally
        {
            currentCredential = string.Empty;
            TryDelete(readyFile);
            TryDelete(stopFile);
            try
            {
                if (factory is not null) await factory.DisposeAsync();
            }
            finally
            {
                if (directory is not null)
                {
                    try
                    {
                        await directory.DisposeAsync();
                        var retained = receipts.ReadRetainedAnchor(settings.Account);
                        Assert.Equal(directory.ObjectId, retained.ObjectId);
                        WriteDurableJson(cleanupReceiptFile, new
                        {
                            status = "known-clean",
                            finalState = "disabled-quarantined-retained",
                            exactOwnedGuid = true,
                            oldUnknownUntouched = true,
                            secretsPersisted = false
                        });
                    }
                    catch
                    {
                        WriteDurableJson(cleanupReceiptFile, new
                        {
                            status = "cleanup-not-confirmed",
                            exactOwnedGuid = true,
                            oldUnknownUntouched = true,
                            secretsPersisted = false
                        });
                        throw;
                    }
                }
            }
        }
    }

    private static void RequireKnownRetainedState(string cleanupReceiptPath, string privateAnchorPath)
    {
        using var cleanup = JsonDocument.Parse(File.ReadAllText(cleanupReceiptPath));
        if (!string.Equals(
                cleanup.RootElement.GetProperty("cleanup_status").GetString(),
                "known-disabled-quarantined-retained",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Prior connected cleanup receipt was not known-retained.");
        if (!Path.GetFullPath(privateAnchorPath).Equals(
                Path.GetFullPath(Environment.GetEnvironmentVariable("HIDP7_T2_GUID_ANCHOR_FILE") ?? string.Empty),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Connected operator anchor path did not match the approved anchor.");
    }

    private static void WriteDurableJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public sealed class ConnectedDirectoryVerifierProbe
    {
        public Guid ExpectedObjectId { get; set; }
        public string ExpectedCanonicalAccount { get; set; } = string.Empty;
        public int VerifyCalls { get; private set; }
        public DirectoryCredentialOutcome? Outcome { get; private set; }
        public long ElapsedMilliseconds { get; private set; }
        public bool RequestedExactObjectId { get; private set; }
        public bool ReturnedExactObjectId { get; private set; }
        public bool CanonicalAccountMatched { get; private set; }

        public void Record(
            Guid requestedObjectId,
            DirectoryCredentialVerificationResult result,
            long elapsedMilliseconds)
        {
            VerifyCalls++;
            Outcome = result.Outcome;
            ElapsedMilliseconds = elapsedMilliseconds;
            RequestedExactObjectId = requestedObjectId == ExpectedObjectId;
            ReturnedExactObjectId = result.Identity?.ObjectId == ExpectedObjectId;
            CanonicalAccountMatched = string.Equals(
                result.Identity?.CanonicalAccount, ExpectedCanonicalAccount, StringComparison.Ordinal);
        }
    }

    private sealed class ConnectedCountingDirectoryCredentialVerifier(
        ProtectedDirectoryCredentialService inner,
        ConnectedDirectoryVerifierProbe probe) : IDirectoryCredentialVerifier
    {
        public async Task<DirectoryCredentialVerificationResult> VerifyCredentialAsync(
            Guid directoryObjectId,
            string password,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await inner.VerifyCredentialAsync(directoryObjectId, password, cancellationToken);
            stopwatch.Stop();
            probe.Record(directoryObjectId, result, stopwatch.ElapsedMilliseconds);
            return result;
        }
    }
}
