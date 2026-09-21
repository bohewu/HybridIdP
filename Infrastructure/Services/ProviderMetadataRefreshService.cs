using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ProviderMetadataRefreshService : IProviderMetadataRefreshService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly ProviderMetadataRefreshOptions _options;
    private readonly TimeProvider _timeProvider;

    public ProviderMetadataRefreshService(
        IApplicationDbContext dbContext,
        HttpClient httpClient,
        IOptions<ProviderMetadataRefreshOptions> options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProviderMetadataRefreshOutcome> RefreshAsync(
        string providerNamespace,
        string stableSubject,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return ProviderMetadataRefreshOutcome.Disabled;
        }

        var request = new ProviderMetadataRequest
        {
            ProviderNamespace = providerNamespace,
            StableSubject = stableSubject
        };
        if (!request.TryValidate(out _))
        {
            return ProviderMetadataRefreshOutcome.InvalidRequest;
        }

        var binding = await _dbContext.ProviderSubjectDirectoryBindings.SingleOrDefaultAsync(
            candidate =>
                candidate.ProviderNamespace == providerNamespace &&
                candidate.StableSubject == stableSubject,
            cancellationToken);
        if (binding is null ||
            !string.Equals(binding.ProviderNamespace, providerNamespace, StringComparison.Ordinal) ||
            !string.Equals(binding.StableSubject, stableSubject, StringComparison.Ordinal))
        {
            return ProviderMetadataRefreshOutcome.BindingNotFound;
        }

        ProviderMetadataResult? result;
        try
        {
            if (!TryGetProtectedEndpoint(out var endpoint))
            {
                await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.Malformed, cancellationToken);
                return ProviderMetadataRefreshOutcome.Malformed;
            }

            using var timeout = new CancellationTokenSource(_options.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request, ProviderMetadataJsonContext.Default.ProviderMetadataRequest)
            };
            message.Headers.TryAddWithoutValidation("X-Internal-Secret", _options.SharedSecret);

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var authenticationFailed = response.StatusCode is
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
                await InvalidateAsync(binding.Id, authenticationFailed
                    ? ProviderMetadataEvidenceState.AuthenticationFailed
                    : ProviderMetadataEvidenceState.Unavailable, cancellationToken);
                return authenticationFailed
                    ? ProviderMetadataRefreshOutcome.AuthenticationFailed
                    : ProviderMetadataRefreshOutcome.Unavailable;
            }

            result = await response.Content.ReadFromJsonAsync(
                ProviderMetadataJsonContext.Default.ProviderMetadataResult,
                linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.TimedOut, cancellationToken);
            return ProviderMetadataRefreshOutcome.TimedOut;
        }
        catch (HttpRequestException)
        {
            await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.Unavailable, cancellationToken);
            return ProviderMetadataRefreshOutcome.Unavailable;
        }
        catch (JsonException)
        {
            await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.Malformed, cancellationToken);
            return ProviderMetadataRefreshOutcome.Malformed;
        }

        if (result is null)
        {
            await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.Missing, cancellationToken);
            return ProviderMetadataRefreshOutcome.Missing;
        }

        if (result.ContractVersion is not null &&
            !string.Equals(result.ContractVersion, ProviderMetadataContract.CurrentVersion, StringComparison.Ordinal))
        {
            await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.Unsupported, cancellationToken);
            return ProviderMetadataRefreshOutcome.Unsupported;
        }

        if (!result.TryValidate(out _) ||
            !string.Equals(result.ProviderNamespace, providerNamespace, StringComparison.Ordinal) ||
            !string.Equals(result.StableSubject, stableSubject, StringComparison.Ordinal))
        {
            await InvalidateAsync(binding.Id, ProviderMetadataEvidenceState.Malformed, cancellationToken);
            return ProviderMetadataRefreshOutcome.Malformed;
        }

        var snapshot = await FindOrCreateSnapshotAsync(binding.Id, cancellationToken);
        var trustedEmail = result.EmailTrustOrigin == EmailTrustOrigin.Unknown
            ? null
            : result.Email?.Trim();
        snapshot.Refresh(
            trustedEmail,
            Map(result.EmailTrustOrigin),
            result.EmailTrustOrigin == EmailTrustOrigin.SourceVerified
                ? result.VerifiedAt?.ToUniversalTime()
                : null,
            _timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return snapshot.EvidenceState == ProviderMetadataEvidenceState.Available
            ? ProviderMetadataRefreshOutcome.Refreshed
            : ProviderMetadataRefreshOutcome.Untrusted;
    }

    private async Task InvalidateAsync(
        Guid bindingId,
        ProviderMetadataEvidenceState evidenceState,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindOrCreateSnapshotAsync(bindingId, cancellationToken);
        snapshot.Invalidate(_timeProvider.GetUtcNow(), evidenceState);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProviderMetadataSnapshot> FindOrCreateSnapshotAsync(
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _dbContext.ProviderMetadataSnapshots.SingleOrDefaultAsync(
            candidate => candidate.ProviderSubjectDirectoryBindingId == bindingId,
            cancellationToken);
        if (snapshot is not null)
        {
            return snapshot;
        }

        snapshot = new ProviderMetadataSnapshot(bindingId, _timeProvider.GetUtcNow());
        await _dbContext.ProviderMetadataSnapshots.AddAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private bool TryGetProtectedEndpoint(out Uri endpoint)
    {
        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out endpoint!) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
             !(_options.AllowPrivateNetworkHttp && endpoint.Scheme == Uri.UriSchemeHttp)) ||
            string.IsNullOrWhiteSpace(_options.SharedSecret) ||
            _options.Timeout <= TimeSpan.Zero)
        {
            endpoint = null!;
            return false;
        }

        return true;
    }

    private static ProviderEmailTrustOrigin Map(EmailTrustOrigin origin) => origin switch
    {
        EmailTrustOrigin.SourceVerified => ProviderEmailTrustOrigin.SourceVerified,
        EmailTrustOrigin.PolicyTrusted => ProviderEmailTrustOrigin.PolicyTrusted,
        _ => ProviderEmailTrustOrigin.Unknown
    };
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProviderMetadataRequest))]
[JsonSerializable(typeof(ProviderMetadataResult))]
internal sealed partial class ProviderMetadataJsonContext : JsonSerializerContext;
