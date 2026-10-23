using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed partial class NativeFieldPolicyService
{
    public async Task<IReadOnlyList<ProfileSyncChangeDto>> ApplyMissingNeutralSourceAsync(ApplicationUser user, Person person,
        NeutralQueryContext context, IReadOnlyDictionary<NativeField, string?> values,
        CancellationToken cancellationToken = default)
    {
        Require(neutralQueries is not null && context.Request.ContractType == "native-profile" &&
            context.LocalAccountId == user.Id && context.PersonId == person.Id && user.PersonId == person.Id, "conflict");
        Require(await neutralQueries!().IsCurrentAsync(context, cancellationToken), "conflict");
        var identity = new NativeFieldSourceIdentity(NativeFieldSource.NeutralQuery, user.Id,
            context.Request.ProviderNamespace, string.Empty, QuerySourceId: context.SourceId);
        Require(await IsAvailableAsync(identity, cancellationToken), "source_unavailable");
        ValidateValues(values, user);
        var document = await ReadMappingDocumentAsync(cancellationToken);
        var mapped = MapNativeSourceValues(identity, values, document, document?.FirstNameFallback);
        var decisions = new List<ProfileSyncChangeDto>();
        ApplySource(user, identity, mapped.Values, true, mapped.UsedFirstNameFallback, true, decisions);
        // A person with several local accounts still needs explicit field ownership assignment.
        var soleAccount = await db.Users.CountAsync(x => x.PersonId == person.Id, cancellationToken) == 1;
        ApplySource(person, identity, mapped.Values, soleAccount, mapped.UsedFirstNameFallback, true, decisions);
        return decisions;
    }

    private async Task<bool> IsNeutralOwnerAvailableAsync(NativeFieldSourceIdentity identity, CancellationToken ct)
    {
        if (neutralQueries is null || identity.LocalAccountId == Guid.Empty || identity.StableSubject != string.Empty ||
            identity.DirectoryObjectId is not null || string.IsNullOrWhiteSpace(identity.QuerySourceId)) return false;
        var state = await neutralQueries().GetConfigurationAsync(ct);
        return state.Configurations.Any(x => x.Capability == "native-profile" && x.WireMode == "neutral-v2" &&
            x.SourceId == identity.QuerySourceId && x.ProviderNamespace == identity.ProviderNamespace &&
            state.Sources.Any(source => source.Capability == x.Capability && source.SourceId == x.SourceId &&
                source.ProviderNamespace == x.ProviderNamespace && source.Available));
    }
}
