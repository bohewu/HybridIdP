using Core.Application.DTOs;

namespace Core.Application.Ports;

public interface ILegacyPasswordSyncTargetResolver
{
    LegacyPasswordSyncMappingResolution Resolve(
        string providerNamespace,
        string stableSubject,
        string? expectedMappingVersion = null);
}

public interface ILegacyPasswordSyncRequestFactory
{
    bool TryCreate(
        Guid operationId,
        LegacyPasswordSyncCohort cohort,
        string providerNamespace,
        string stableSubject,
        string? expectedMappingVersion,
        string password,
        out LegacyPasswordSyncRequest? request,
        out LegacyPasswordSyncTarget? target);
}
