using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Hardened consumer for Provider Proof Contract 1.0.
/// </summary>
public sealed class ProviderProofProvider : IProofProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderProofOptions _options;

    public ProviderProofProvider(HttpClient httpClient, IOptions<ProviderProofOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ProofResult> ProveAsync(
        ProofRequest request,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!request.TryValidate(out _) || string.IsNullOrEmpty(password) ||
            !TryGetProtectedEndpoint(out var endpoint))
        {
            return new ProofResult { Outcome = ProofOutcome.Malformed };
        }

        try
        {
            using var timeout = new CancellationTokenSource(_options.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new ProviderProofLoginRequest(
                    request.AccountName,
                    password,
                    request.ContractVersion,
                    request.RequestedEmailOtpPolicy),
                    ProviderProofJsonContext.Default.ProviderProofLoginRequest)
            };
            message.Headers.TryAddWithoutValidation("X-Internal-Secret", _options.SharedSecret);

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new ProofResult { Outcome = ProofOutcome.Unavailable };
            }

            var proof = await response.Content.ReadFromJsonAsync(
                ProviderProofJsonContext.Default.ProofResult,
                linked.Token);
            if (proof is not null)
            {
                proof = proof with
                {
                    RequiredActions = proof.RequiredActions ?? [],
                    Profile = proof.Profile is null
                        ? null
                        : proof.Profile with { AssuredFields = proof.Profile.AssuredFields ?? [] }
                };
            }

            return proof is not null && proof.TryValidate(out _)
                ? proof
                : new ProofResult { Outcome = ProofOutcome.Malformed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProofResult { Outcome = ProofOutcome.Timeout };
        }
        catch
        {
            return new ProofResult { Outcome = ProofOutcome.Unavailable };
        }
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

}

internal sealed record ProviderProofLoginRequest(
    string AccountName,
    string Password,
    string ContractVersion,
    EmailOtpPolicy RequestedEmailOtpPolicy);

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProviderProofLoginRequest))]
[JsonSerializable(typeof(ProofResult))]
internal sealed partial class ProviderProofJsonContext : JsonSerializerContext;
