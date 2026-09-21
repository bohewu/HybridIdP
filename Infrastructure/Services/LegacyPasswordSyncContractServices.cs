using Core.Application.DTOs;
using Core.Application.Ports;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ConfiguredLegacyPasswordSyncTargetResolver : ILegacyPasswordSyncTargetResolver
{
    private readonly LegacyPasswordSyncOptions _options;

    public ConfiguredLegacyPasswordSyncTargetResolver(IOptions<LegacyPasswordSyncOptions> options)
    {
        _options = options.Value;
    }

    public LegacyPasswordSyncMappingResolution Resolve(
        string providerNamespace,
        string stableSubject,
        string? expectedMappingVersion = null)
    {
        if (!_options.Enabled)
        {
            return new(LegacyPasswordSyncMappingOutcome.Disabled);
        }

        if (!LegacyPasswordSyncOptionsValidator.IsExactValue(providerNamespace) ||
            !LegacyPasswordSyncOptionsValidator.IsExactValue(stableSubject) ||
            !LegacyPasswordSyncOptionsValidator.IsExactValue(_options.TrustedProviderNamespace) ||
            !LegacyPasswordSyncOptionsValidator.IsExactValue(_options.ActiveMappingVersion) ||
            _options.Mappings is null)
        {
            return new(LegacyPasswordSyncMappingOutcome.Malformed);
        }

        if (!string.Equals(providerNamespace, _options.TrustedProviderNamespace, StringComparison.Ordinal))
        {
            return new(LegacyPasswordSyncMappingOutcome.Missing);
        }

        var enabledMappings = _options.Mappings.Where(mapping => mapping.Enabled).ToArray();
        if (enabledMappings.Any(mapping =>
                !LegacyPasswordSyncOptionsValidator.IsExactValue(mapping.ProviderNamespace) ||
                !LegacyPasswordSyncOptionsValidator.IsExactValue(mapping.StableSubject) ||
                !LegacyPasswordSyncOptionsValidator.IsExactValue(mapping.MappingVersion) ||
                mapping.AccountIdentity == Guid.Empty ||
                !string.Equals(mapping.ProviderNamespace, _options.TrustedProviderNamespace, StringComparison.Ordinal)))
        {
            return new(LegacyPasswordSyncMappingOutcome.Malformed);
        }

        if (enabledMappings.GroupBy(mapping => mapping.AccountIdentity).Any(group => group.Count() != 1))
        {
            return new(LegacyPasswordSyncMappingOutcome.Duplicate);
        }

        var matches = enabledMappings.Where(mapping =>
            string.Equals(mapping.ProviderNamespace, providerNamespace, StringComparison.Ordinal) &&
            string.Equals(mapping.StableSubject, stableSubject, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0)
        {
            return new(LegacyPasswordSyncMappingOutcome.Missing);
        }

        if (matches.Length > 1)
        {
            var first = matches[0];
            var contradictory = matches.Skip(1).Any(mapping =>
                mapping.AccountIdentity != first.AccountIdentity ||
                !string.Equals(mapping.MappingVersion, first.MappingVersion, StringComparison.Ordinal));
            return new(contradictory
                ? LegacyPasswordSyncMappingOutcome.Contradictory
                : LegacyPasswordSyncMappingOutcome.Duplicate);
        }

        var match = matches[0];
        if (!string.Equals(match.MappingVersion, _options.ActiveMappingVersion, StringComparison.Ordinal) ||
            expectedMappingVersion is not null &&
            !string.Equals(match.MappingVersion, expectedMappingVersion, StringComparison.Ordinal))
        {
            return new(LegacyPasswordSyncMappingOutcome.Stale);
        }

        return new(
            LegacyPasswordSyncMappingOutcome.Resolved,
            new LegacyPasswordSyncTarget(match.AccountIdentity, match.MappingVersion!));
    }
}

public sealed class LegacyPasswordSyncRequestFactory : ILegacyPasswordSyncRequestFactory
{
    private readonly LegacyPasswordSyncOptions _options;
    private readonly ILegacyPasswordSyncTargetResolver _targetResolver;

    public LegacyPasswordSyncRequestFactory(
        IOptions<LegacyPasswordSyncOptions> options,
        ILegacyPasswordSyncTargetResolver targetResolver)
    {
        _options = options.Value;
        _targetResolver = targetResolver;
    }

    public bool TryCreate(
        Guid operationId,
        LegacyPasswordSyncCohort cohort,
        string providerNamespace,
        string stableSubject,
        string? expectedMappingVersion,
        string password,
        out LegacyPasswordSyncRequest? request,
        out LegacyPasswordSyncTarget? target)
    {
        request = null;
        target = null;
        if (!_options.Enabled || operationId == Guid.Empty || string.IsNullOrEmpty(password) ||
            !Enum.IsDefined(cohort) || !_options.IsCohortEnabled(cohort) ||
            new LegacyPasswordSyncOptionsValidator().Validate(null, _options).Failed)
        {
            return false;
        }

        var resolution = _targetResolver.Resolve(providerNamespace, stableSubject, expectedMappingVersion);
        if (resolution is not { Outcome: LegacyPasswordSyncMappingOutcome.Resolved, Target: not null } ||
            !LegacyPasswordSyncContract.IsCanonicalAccountIdentity(resolution.Target.AccountIdentity))
        {
            return false;
        }

        target = resolution.Target;
        request = new LegacyPasswordSyncRequest
        {
            OperationId = operationId,
            AccountIdentity = target.AccountIdentity,
            Password = password
        };
        return true;
    }
}

public static class LegacyPasswordSyncHttpClient
{
    public const string Name = "LegacyPasswordSync";

    public static HttpMessageHandler CreatePrimaryHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    };
}
