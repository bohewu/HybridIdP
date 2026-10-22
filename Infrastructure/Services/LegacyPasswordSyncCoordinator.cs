using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Application.Utilities;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class LegacyPasswordSyncCoordinator(
    ApplicationDbContext dbContext,
    ILegacyPasswordSyncAttemptStore attemptStore,
    ILegacyPasswordSyncRequestFactory requestFactory,
    ILegacyPasswordSyncTransport transport,
    INeutralQueryService? queries = null,
    IOptions<LegacyPasswordSyncOptions>? options = null) : ILegacyPasswordSyncCoordinator
{
    public async Task<LegacyPasswordSyncResult> SynchronizeAsync(
        LegacyPasswordSyncSourceReference source,
        LegacyPasswordSyncCohort cohort,
        Guid localAccountId,
        Guid providerSubjectDirectoryBindingId,
        string expectedAccountConcurrencyStamp,
        string expectedSecurityStamp,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesCohort(source.Kind, cohort) || localAccountId == Guid.Empty ||
            providerSubjectDirectoryBindingId == Guid.Empty || string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(expectedAccountConcurrencyStamp) ||
            string.IsNullOrWhiteSpace(expectedSecurityStamp) || cancellationToken.IsCancellationRequested)
        {
            return new(LegacyPasswordSyncResultOutcome.NotEligible);
        }

        Guid? claimedOperationId = null;
        try
        {
            var binding = await LoadCurrentBindingAsync(
                localAccountId,
                providerSubjectDirectoryBindingId,
                expectedAccountConcurrencyStamp,
                expectedSecurityStamp,
                cancellationToken);
            if (binding is null) return new(LegacyPasswordSyncResultOutcome.NotEligible);
            var resolution = queries is null ? new NeutralQueryResolution("Legacy") :
                await queries.ResolveAsync("password-sync", localAccountId, cancellationToken: cancellationToken);
            NeutralQueryContext? query = null;
            LegacyPasswordSyncTarget? target = null;
            if (resolution.Status == "Ready" && resolution.Context is not null)
            {
                if (options?.Value is not { Enabled: true } enabled || !enabled.IsCohortEnabled(cohort))
                    return new(LegacyPasswordSyncResultOutcome.NotEligible);
                query = resolution.Context;
            }
            else if (resolution.Status != "Legacy" || !requestFactory.TryCreate(
                    Guid.NewGuid(),
                    cohort,
                    binding.ProviderNamespace,
                    binding.StableSubject,
                    expectedMappingVersion: null,
                    password,
                    out _,
                    out target) || target is null)
            {
                return new(LegacyPasswordSyncResultOutcome.NotEligible);
            }

            // In v2 this internal key scopes the account's single destination lane; it is never
            // sent as an upstream identity. The fingerprint binds the actual approved query,
            // rule, source and revisions while the producer owns its resolved target receipt.
            var identity = new LegacyPasswordSyncAttemptIdentity(
                localAccountId,
                binding,
                query is null ? target!.AccountIdentityGuid : localAccountId,
                query is null ? target!.MappingVersion : "neutral-v2:" + query.Fingerprint,
                query);
            var reservation = await attemptStore.ReserveAsync(source, identity, cancellationToken);
            if (reservation.Outcome == LegacyPasswordSyncReservationOutcome.DuplicateSource)
            {
                return new(LegacyPasswordSyncResultOutcome.DuplicateSource, reservation.Attempt?.OperationId);
            }
            if (reservation is not { Outcome: LegacyPasswordSyncReservationOutcome.Reserved, Attempt: not null })
            {
                return new(LegacyPasswordSyncResultOutcome.Blocked, reservation.Attempt?.OperationId);
            }

            var claim = await attemptStore.ClaimAsync(
                reservation.Attempt.OperationId,
                reservation.Attempt.Version,
                source,
                identity,
                cancellationToken);
            if (claim is not { Outcome: LegacyPasswordSyncClaimOutcome.Claimed, Attempt: not null })
            {
                return new(LegacyPasswordSyncResultOutcome.Blocked, reservation.Attempt.OperationId);
            }

            var operationId = claim.Attempt.OperationId;
            var claimedVersion = claim.Attempt.Version;
            claimedOperationId = operationId;
            var currentBinding = await LoadCurrentBindingAsync(
                localAccountId,
                providerSubjectDirectoryBindingId,
                expectedAccountConcurrencyStamp,
                expectedSecurityStamp,
                CancellationToken.None);
            LegacyPasswordSyncRequest? request = null;
            var stale = currentBinding != binding || cancellationToken.IsCancellationRequested;
            if (query is not null)
            {
                query = query with { Request = query.Request with { OperationId = operationId.ToString("D"), Password = password } };
                stale |= options?.Value is not { Enabled: true } enabled || !enabled.IsCohortEnabled(cohort) ||
                    !await queries!.IsCurrentAsync(query, CancellationToken.None);
            }
            else if (!stale)
            {
                stale = !requestFactory.TryCreate(
                    operationId,
                    cohort,
                    currentBinding!.ProviderNamespace,
                    currentBinding.StableSubject,
                    identity.MappingVersion,
                    password,
                    out request,
                    out var currentTarget) ||
                request is null || currentTarget is null ||
                currentTarget.AccountIdentityGuid != identity.AccountIdentity ||
                !string.Equals(currentTarget.MappingVersion, identity.MappingVersion, StringComparison.Ordinal);
            }
            if (stale)
            {
                return await RecordAsync(
                    operationId,
                    claimedVersion,
                    LegacyPasswordSyncTerminalStatus.Failed,
                    "pre_dispatch_stale",
                    LegacyPasswordSyncResultOutcome.Failed);
            }

            LegacyPasswordSyncTransportResult transportResult;
            try
            {
                transportResult = query is null ? await transport.SendAsync(request!, cancellationToken) :
                    await transport.SendAsync(query, cancellationToken);
            }
            catch
            {
                transportResult = new(LegacyPasswordSyncTransportOutcome.PossibleDispatchFailure);
            }

            if (transportResult.Outcome == LegacyPasswordSyncTransportOutcome.PreDispatchRejected)
            {
                return await RecordAsync(
                    operationId,
                    claimedVersion,
                    LegacyPasswordSyncTerminalStatus.Failed,
                    "pre_dispatch_rejected",
                    LegacyPasswordSyncResultOutcome.Failed);
            }

            var terminalStatus = LegacyPasswordSyncTerminalStatus.Unknown;
            var outcome = "response_untrusted";
            if (transportResult.Outcome != LegacyPasswordSyncTransportOutcome.TrustedResponse ||
                !(query is null
                    ? TryClassifyResponse(transportResult.ResponseJson, operationId, out terminalStatus, out outcome)
                    : TryClassifyNeutralResponse(transportResult.NeutralResponse, query.Request, out terminalStatus, out outcome)))
            {
                return await RecordAsync(
                    operationId,
                    claimedVersion,
                    LegacyPasswordSyncTerminalStatus.Unknown,
                    "dispatch_unknown",
                    LegacyPasswordSyncResultOutcome.Unknown);
            }

            return await RecordAsync(operationId, claimedVersion, terminalStatus, outcome, terminalStatus switch
            {
                LegacyPasswordSyncTerminalStatus.Succeeded => LegacyPasswordSyncResultOutcome.Succeeded,
                LegacyPasswordSyncTerminalStatus.Failed => LegacyPasswordSyncResultOutcome.Failed,
                _ => LegacyPasswordSyncResultOutcome.Unknown
            });
        }
        catch
        {
            return claimedOperationId is Guid operationId
                ? new(LegacyPasswordSyncResultOutcome.Unknown, operationId)
                : new(LegacyPasswordSyncResultOutcome.Failed);
        }
    }

    private static bool TryClassifyNeutralResponse(NeutralQueryResponse? response, NeutralQueryRequest request,
        out LegacyPasswordSyncTerminalStatus status, out string outcome)
    {
        status = LegacyPasswordSyncTerminalStatus.Unknown;
        outcome = "response_untrusted";
        if (response is null || response.ContractType != request.ContractType || response.RequestId != request.RequestId ||
            response.ProviderNamespace != request.ProviderNamespace || response.OperationId != request.OperationId) return false;
        if (response.QueryOutcome is "NotFound" or "Ambiguous")
        {
            status = LegacyPasswordSyncTerminalStatus.Failed;
            outcome = "neutral_no_target";
            return true;
        }
        if (response is not { QueryOutcome: "Found", Result: { } result }) return false;
        (status, outcome) = result.GetProperty("outcome").GetString() switch
        {
            "Success" => (LegacyPasswordSyncTerminalStatus.Succeeded, "neutral_success"),
            "NoOp" => (LegacyPasswordSyncTerminalStatus.Failed, "neutral_no_op"),
            "Failed" => (LegacyPasswordSyncTerminalStatus.Failed, "neutral_failed"),
            "PartialSuccess" => (LegacyPasswordSyncTerminalStatus.Unknown, "neutral_partial_success"),
            "CommitUnknown" => (LegacyPasswordSyncTerminalStatus.Unknown, "neutral_commit_unknown"),
            _ => (LegacyPasswordSyncTerminalStatus.Unknown, "response_untrusted")
        };
        return outcome != "response_untrusted";
    }

    private async Task<LegacyPasswordSyncBindingReference?> LoadCurrentBindingAsync(
        Guid localAccountId,
        Guid bindingId,
        string expectedAccountConcurrencyStamp,
        string expectedSecurityStamp,
        CancellationToken cancellationToken)
    {
        var accountMatches = await dbContext.Users.AsNoTracking().AnyAsync(
            user => user.Id == localAccountId && user.IsActive && !user.IsDeleted &&
                    user.ConcurrencyStamp == expectedAccountConcurrencyStamp &&
                    user.SecurityStamp == expectedSecurityStamp,
            cancellationToken);
        if (!accountMatches)
        {
            return null;
        }

        return await dbContext.ProviderSubjectDirectoryBindings.AsNoTracking()
            .Where(binding => binding.Id == bindingId && binding.LocalAccountId == localAccountId)
            .Select(binding => new LegacyPasswordSyncBindingReference(
                binding.Id,
                binding.ProviderNamespace,
                binding.StableSubject,
                binding.DirectoryObjectId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<LegacyPasswordSyncResult> RecordAsync(
        Guid operationId,
        long expectedVersion,
        LegacyPasswordSyncTerminalStatus status,
        string sanitizedOutcome,
        LegacyPasswordSyncResultOutcome resultOutcome)
    {
        try
        {
            return await attemptStore.RecordResultAsync(
                operationId,
                expectedVersion,
                status,
                sanitizedOutcome,
                CancellationToken.None)
                ? new(resultOutcome, operationId)
                : new(LegacyPasswordSyncResultOutcome.Unknown, operationId);
        }
        catch
        {
            return new(LegacyPasswordSyncResultOutcome.Unknown, operationId);
        }
    }

    private static bool TryClassifyResponse(
        string? responseJson,
        Guid operationId,
        out LegacyPasswordSyncTerminalStatus status,
        out string sanitizedOutcome)
    {
        status = LegacyPasswordSyncTerminalStatus.Unknown;
        sanitizedOutcome = "response_untrusted";
        if (!LegacyPasswordSyncContract.TryParseResponse(responseJson, operationId, out var response) ||
            response is null)
        {
            return false;
        }

        (status, sanitizedOutcome) = response.Outcome switch
        {
            LegacyPasswordSyncOutcome.Success =>
                (LegacyPasswordSyncTerminalStatus.Succeeded, "legacy_success"),
            LegacyPasswordSyncOutcome.NoOp =>
                (LegacyPasswordSyncTerminalStatus.Failed, "legacy_no_op"),
            LegacyPasswordSyncOutcome.Failed =>
                (LegacyPasswordSyncTerminalStatus.Failed, "legacy_failed"),
            LegacyPasswordSyncOutcome.PartialSuccess =>
                (LegacyPasswordSyncTerminalStatus.Unknown, "legacy_partial_success"),
            LegacyPasswordSyncOutcome.CommitUnknown =>
                (LegacyPasswordSyncTerminalStatus.Unknown, "legacy_commit_unknown"),
            _ => throw new InvalidOperationException("Undefined Legacy password sync outcome.")
        };
        return true;
    }

    private static bool MatchesCohort(LegacyPasswordSyncSourceKind sourceKind, LegacyPasswordSyncCohort cohort) =>
        (sourceKind, cohort) switch
        {
            (LegacyPasswordSyncSourceKind.NativeRecovery, LegacyPasswordSyncCohort.CompletedDirectoryRecovery) => true,
            (LegacyPasswordSyncSourceKind.RequiredChange, LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange) => true,
            (LegacyPasswordSyncSourceKind.Stage2Migration, LegacyPasswordSyncCohort.Stage2Migration) => true,
            _ => false
        };
}

public sealed class LegacyPasswordSyncHttpTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<LegacyPasswordSyncOptions> options,
    ILogger<LegacyPasswordSyncHttpTransport>? logger = null,
    INeutralQueryService? queries = null) : ILegacyPasswordSyncTransport
{
    private const int MaximumResponseBytes = 64 * 1024;
    private readonly LegacyPasswordSyncOptions _options = options.Value;

    public async Task<LegacyPasswordSyncTransportResult> SendAsync(NeutralQueryContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Transport is null || context.DirectoryTransport is not null || !_options.Enabled || context.Request.ContractType != "password-sync" || cancellationToken.IsCancellationRequested ||
            !NeutralQueryJson.TrySerializeRequest(context.Request, out var body))
            return new(LegacyPasswordSyncTransportOutcome.PreDispatchRejected);
        var dispatched = false;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, context.Transport.Endpoint)
            {
                Content = new ByteArrayContent(body)
            };
            message.Content.Headers.ContentType = new("application/json");
            message.Headers.Add("X-Internal-Secret", context.Transport.SharedSecret);
            message.Headers.Add("X-Provider-Contract", "password-sync/2.0");
            message.Headers.CacheControl = new() { NoStore = true };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(context.Transport.Timeout);
            if (queries is null || !await queries.IsCurrentAsync(context, deadline.Token))
                return new(LegacyPasswordSyncTransportOutcome.PreDispatchRejected);
            var client = httpClientFactory.CreateClient(LegacyPasswordSyncHttpClient.Name);
            dispatched = true;
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > MaximumResponseBytes)
                return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
            var buffer = new byte[MaximumResponseBytes + 1];
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (count > MaximumResponseBytes || response.Content.Headers.ContentLength is { } length && length != count ||
                !NeutralQueryJson.TryReadResponse((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(),
                    buffer.AsMemory(0, count), context.Request, out var result))
                return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
            return new(LegacyPasswordSyncTransportOutcome.TrustedResponse, NeutralResponse: result);
        }
        catch
        {
            return new(dispatched ? LegacyPasswordSyncTransportOutcome.PossibleDispatchFailure :
                LegacyPasswordSyncTransportOutcome.PreDispatchRejected);
        }
        finally { Array.Clear(body); }
    }

    public async Task<LegacyPasswordSyncTransportResult> SendAsync(
        LegacyPasswordSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !LegacyPasswordSyncContract.TryValidateRequest(request) ||
            cancellationToken.IsCancellationRequested ||
            !Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint) ||
            !LegacyPasswordSyncOptionsValidator.IsProtectedEndpoint(
                _options.Endpoint,
                _options.AllowPrivateNetworkHttp) ||
            !LegacyPasswordSyncOptionsValidator.IsExactSecret(_options.SharedSecret) ||
            new LegacyPasswordSyncOptionsValidator().Validate(null, _options).Failed)
        {
            logger?.LogWarning(
                "Legacy password sync request was rejected before dispatch for operation {OperationId}.",
                request?.OperationId);
            return new(LegacyPasswordSyncTransportOutcome.PreDispatchRejected);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            if (!message.Headers.TryAddWithoutValidation("X-Internal-Secret", _options.SharedSecret))
            {
                logger?.LogWarning(
                    "Legacy password sync authentication header was rejected before dispatch for operation {OperationId}.",
                    request.OperationId);
                return new(LegacyPasswordSyncTransportOutcome.PreDispatchRejected);
            }

            using var timeout = new CancellationTokenSource(_options.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await httpClientFactory.CreateClient(LegacyPasswordSyncHttpClient.Name).SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentType?.MediaType is not string mediaType ||
                !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogWarning(
                    "Legacy password sync returned an untrusted HTTP response for operation {OperationId}.",
                    request.OperationId);
                return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
            }

            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength > MaximumResponseBytes)
            {
                logger?.LogWarning(
                    "Legacy password sync returned an oversized response for operation {OperationId}.",
                    request.OperationId);
                return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var remaining = MaximumResponseBytes - checked((int)buffer.Length);
                var read = await stream.ReadAsync(
                    chunk.AsMemory(0, Math.Min(chunk.Length, remaining + 1)),
                    linked.Token);
                if (read == 0)
                {
                    string responseJson;
                    try
                    {
                        responseJson = new System.Text.UTF8Encoding(false, true).GetString(buffer.ToArray());
                    }
                    catch (System.Text.DecoderFallbackException)
                    {
                        logger?.LogWarning(
                            "Legacy password sync returned invalid UTF-8 for operation {OperationId}.",
                            request.OperationId);
                        return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
                    }

                    if (!LegacyPasswordSyncContract.TryParseResponse(
                            responseJson,
                            request.OperationId,
                            out _))
                    {
                        logger?.LogWarning(
                            "Legacy password sync returned an invalid contract response for operation {OperationId}.",
                            request.OperationId);
                        return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
                    }

                    return new(LegacyPasswordSyncTransportOutcome.TrustedResponse, responseJson);
                }

                if (read > remaining)
                {
                    logger?.LogWarning(
                        "Legacy password sync returned an oversized response for operation {OperationId}.",
                        request.OperationId);
                    return new(LegacyPasswordSyncTransportOutcome.UntrustedResponse);
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read), linked.Token);
            }
        }
        catch
        {
            logger?.LogWarning(
                "Legacy password sync transport failed after dispatch may have occurred for operation {OperationId}.",
                request.OperationId);
            return new(LegacyPasswordSyncTransportOutcome.PossibleDispatchFailure);
        }
    }
}
