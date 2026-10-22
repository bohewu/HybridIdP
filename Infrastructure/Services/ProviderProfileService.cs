using System.Security.Cryptography;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ProviderProfileService(
    IApplicationDbContext db, ProviderProfileClient client, IOptions<ProviderProfileOptions> options,
    TimeProvider? timeProvider = null, INeutralQueryService? queries = null) : IProviderProfileService, IProviderProfileSnapshotReader, IProviderProfilePreviewReader
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<(Guid User, string Source), IReadOnlyDictionary<string, JsonElement>> _requestValues = [];
    private static readonly IReadOnlyDictionary<string, JsonElement> Empty = new Dictionary<string, JsonElement>();

    public async Task RefreshAfterLoginAsync(ApplicationUser user, string providerNamespace, string stableSubject,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return;
        foreach (var (name, source) in options.Value.Sources.Where(p => p.Value.ProviderNamespace == providerNamespace ||
                     options.Value.QuerySources.ContainsKey(p.Key)))
        {
            _requestValues.Remove((user.Id, name));
            // The login tuple must match the durable source selected for this Person.
            await ResolveAsync(user, name, source, true, stableSubject, cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetPropertiesAsync(ApplicationUser user,
        string source, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || !options.Value.Sources.TryGetValue(source, out var configured)) return Empty;
        // Scope enrichment may be called more than once in the same issuance request.
        return (await ResolveAsync(user, source, configured, false, null, cancellationToken)).Properties;
    }

    public Task<ProviderProfileSnapshotResult> ReadSnapshotAsync(ApplicationUser user, string source,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return Task.FromResult(new ProviderProfileSnapshotResult("disabled", Empty));
        if (!options.Value.Sources.TryGetValue(source, out var configured))
            return Task.FromResult(new ProviderProfileSnapshotResult("unconfigured", Empty));
        return ResolveAsync(user, source, configured, false, null, cancellationToken, readOnly: true);
    }

    public Task<ProviderProfileSnapshotResult> ReadForPreviewAsync(ApplicationUser user, string source,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return Task.FromResult(new ProviderProfileSnapshotResult("disabled", Empty));
        if (!options.Value.Sources.TryGetValue(source, out var configured))
            return Task.FromResult(new ProviderProfileSnapshotResult("unconfigured", Empty));
        return ResolveAsync(user, source, configured, true, null, cancellationToken, readOnly: true, preview: true);
    }

    private async Task<ProviderProfileSnapshotResult> ResolveAsync(ApplicationUser user, string sourceName,
        ProviderProfileSourceOptions source, bool force, string? loginSubject, CancellationToken cancellationToken,
        bool readOnly = false, bool preview = false)
    {
        if (queries is not null)
        {
            var query = await queries.ResolveAsync("profile", user.Id, sourceName, cancellationToken: cancellationToken);
            if (query.Status != "Legacy")
                return query.Context is { } context && query.Status == "Ready"
                    ? await ResolveNeutralAsync(user, sourceName, source, context, force, readOnly, preview, cancellationToken)
                    : new("query_unavailable", Empty);
        }
        var queryRevision = await LegacyQueryRevisionAsync(cancellationToken);
        var currentUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        if (currentUser is not { IsActive: true, IsDeleted: false, PersonId: { } personId }) return new("account_ineligible", Empty);
        var person = await (readOnly ? db.Persons.AsNoTracking() : db.Persons).SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null || !person.CanAuthenticate()) return new("person_ineligible", Empty);

        var links = await (from link in db.UserLogins.AsNoTracking()
                           join account in db.Users.AsNoTracking() on link.UserId equals account.Id
                           where account.PersonId == personId && account.IsActive && !account.IsDeleted &&
                               link.LoginProvider == source.ProviderNamespace
                           select new { account.Id, link.LoginProvider, link.ProviderKey }).Take(2).ToListAsync(cancellationToken);
        if (links.Count != 1 || links[0].LoginProvider != source.ProviderNamespace || (loginSubject is not null &&
            (links[0].Id != user.Id || links[0].ProviderKey != loginSubject))) return new("link_missing_or_ambiguous", Empty);

        var linkIdentity = links[0];
        Dictionary<string, ProfileSnapshot> snapshots;
        try { snapshots = preview ? [] : JsonSerializer.Deserialize<Dictionary<string, ProfileSnapshot>>(person.ProviderProfilesJson ?? "{}") ?? []; }
        catch (JsonException) { return new("snapshot_invalid", Empty); }
        snapshots.TryGetValue(sourceName, out var snapshot);
        var now = _clock.GetUtcNow();
        var policyHash = ComputeHash(JsonSerializer.SerializeToElement(new
        {
            source.Endpoint, source.ProviderNamespace, source.AllowPrivateNetworkHttp,
            Properties = source.AllowedProperties.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()
        }));
        var matching = snapshot is not null && snapshot.AccountId == linkIdentity.Id &&
            snapshot.ProviderNamespace == source.ProviderNamespace && snapshot.StableSubject == linkIdentity.ProviderKey &&
            snapshot.PolicyHash == policyHash && snapshot.QueryFingerprint == queryRevision;
        if (!readOnly && !force && matching && _requestValues.TryGetValue((user.Id, sourceName), out var requestValues))
            return new("confirmed", requestValues, snapshot!.ConfirmedAt);
        if (!force && matching && snapshot!.ConfirmedAt is { } confirmed && confirmed <= now &&
            options.Value.MaximumAge > TimeSpan.Zero && now - confirmed < options.Value.MaximumAge)
        {
            if (!readOnly) _requestValues[(user.Id, sourceName)] = snapshot.Properties;
            return new("confirmed", snapshot.Properties, confirmed, confirmed + options.Value.MaximumAge);
        }

        if (readOnly && !preview)
            return new(snapshot is null ? "snapshot_missing" : !matching ? "configuration_changed" :
                snapshot.ConfirmedAt is null ? "unconfirmed" : "expired", Empty,
                matching ? snapshot!.ConfirmedAt : null,
                matching && snapshot!.ConfirmedAt is { } at ? at + options.Value.MaximumAge : null);

        var result = await client.FetchAsync(new ProviderProfileRequest
        {
            ProviderNamespace = source.ProviderNamespace, StableSubject = linkIdentity.ProviderKey
        }, source, cancellationToken);
        // The network await is not an association lock. Recheck the exact tuple and current Person.
        var currentLinks = await (from link in db.UserLogins.AsNoTracking()
                                  join account in db.Users.AsNoTracking() on link.UserId equals account.Id
                                  where account.PersonId == personId && account.IsActive && !account.IsDeleted &&
                                      link.LoginProvider == source.ProviderNamespace
                                  select new { account.Id, link.LoginProvider, link.ProviderKey })
                                  .Take(2).ToListAsync(cancellationToken);
        var currentPerson = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        var requestingAccount = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        if (queryRevision != await LegacyQueryRevisionAsync(cancellationToken))
            return new("configuration_changed", Empty);
        if (currentLinks.Count != 1 || currentLinks[0].Id != linkIdentity.Id ||
            currentLinks[0].LoginProvider != source.ProviderNamespace || currentLinks[0].ProviderKey != linkIdentity.ProviderKey ||
            requestingAccount is not { IsActive: true, IsDeleted: false } || requestingAccount.PersonId != personId ||
            currentPerson is null || !currentPerson.CanAuthenticate()) return new("account_ineligible", Empty);
        var properties = result?.ExtraProperties.Where(p => source.AllowedProperties.TryGetValue(p.Key, out var type) &&
            ProviderProfileContract.IsValueOfType(p.Value, type))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        // Administrative preview uses fresh values only for this request. It never
        // mutates the Person, the ordinary request cache or persisted confirmation.
        if (preview)
            return new(properties is null ? "unavailable" : "confirmed", properties is null ? Empty : properties,
                properties is null ? null : _clock.GetUtcNow());
        snapshot = matching ? snapshot! : new ProfileSnapshot
        {
            AccountId = linkIdentity.Id, ProviderNamespace = source.ProviderNamespace,
            StableSubject = linkIdentity.ProviderKey, PolicyHash = policyHash, QueryFingerprint = queryRevision
        };
        snapshot.ConfirmedAt = properties is null ? null : now;
        if (properties is not null)
        {
            var hash = ComputePropertyHash(properties);
            if (snapshot.Hash != hash) snapshot.Properties = properties;
            snapshot.Hash = hash;
        }
        snapshots[sourceName] = snapshot;
        person.ProviderProfilesJson = JsonSerializer.Serialize(snapshots);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            db.Detach(person);
            return new("concurrent_change", Empty);
        }
        var accepted = properties is null ? Empty : snapshot.Properties;
        _requestValues[(user.Id, sourceName)] = accepted;
        return new(properties is null ? "unconfirmed" : "confirmed", accepted, snapshot.ConfirmedAt);
    }

    private async Task<ProviderProfileSnapshotResult> ResolveNeutralAsync(ApplicationUser user, string sourceName,
        ProviderProfileSourceOptions source, NeutralQueryContext context, bool force, bool readOnly, bool preview,
        CancellationToken ct)
    {
        if (context.PersonId is not { } personId || source.ProviderNamespace != context.Request.ProviderNamespace)
            return new("account_ineligible", Empty);
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == user.Id, ct);
        var person = await (readOnly ? db.Persons.AsNoTracking() : db.Persons).SingleOrDefaultAsync(x => x.Id == personId, ct);
        if (account is not { IsActive: true, IsDeleted: false } || account.PersonId != personId ||
            person is null || !person.CanAuthenticate()) return new("account_ineligible", Empty);
        // Multiple accounts sharing a Person must never replace each other's source confirmation.
        var key = sourceName + ":v2:" + user.Id.ToString("D");
        Dictionary<string, ProfileSnapshot> snapshots;
        try { snapshots = preview ? [] : JsonSerializer.Deserialize<Dictionary<string, ProfileSnapshot>>(person.ProviderProfilesJson ?? "{}") ?? []; }
        catch (JsonException) { return new("snapshot_invalid", Empty); }
        var approved = new Dictionary<string, string>(source.AllowedProperties, StringComparer.Ordinal);
        var policyHash = ComputeHash(JsonSerializer.SerializeToElement(approved.OrderBy(x => x.Key, StringComparer.Ordinal)));
        snapshots.TryGetValue(key, out var snapshot);
        var matching = snapshot is not null && snapshot.AccountId == user.Id &&
            snapshot.ProviderNamespace == context.Request.ProviderNamespace && snapshot.QueryFingerprint == context.Fingerprint &&
            snapshot.PolicyHash == policyHash;
        var now = _clock.GetUtcNow();
        if (!force && matching && snapshot!.ConfirmedAt is { } confirmed && confirmed <= now &&
            options.Value.MaximumAge > TimeSpan.Zero && now - confirmed < options.Value.MaximumAge &&
            await queries!.IsCurrentAsync(context, ct))
            return new("confirmed", snapshot.Properties, confirmed, confirmed + options.Value.MaximumAge);
        if (readOnly && !preview)
            return new(snapshot is null ? "snapshot_missing" : !matching ? "configuration_changed" : "expired", Empty);

        var result = await client.FetchNeutralAsync(context, ct);
        var current = await db.Persons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId, ct);
        if (!options.Value.Enabled || !options.Value.Sources.TryGetValue(sourceName, out var currentSource) ||
            currentSource.ProviderNamespace != context.Request.ProviderNamespace ||
            policyHash != ComputeHash(JsonSerializer.SerializeToElement(currentSource.AllowedProperties.OrderBy(x => x.Key, StringComparer.Ordinal))) ||
            !await queries!.IsCurrentAsync(context, ct) || current is null || !current.CanAuthenticate())
            return new("configuration_changed", Empty);
        var properties = result?.ExtraProperties.Where(x => approved.TryGetValue(x.Key, out var type) &&
            ProviderProfileContract.IsValueOfType(x.Value, type)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (preview) return new(properties is null ? "unavailable" : "confirmed", properties is null ? Empty : properties,
            properties is null ? null : _clock.GetUtcNow());
        snapshot = new ProfileSnapshot
        {
            AccountId = user.Id, ProviderNamespace = context.Request.ProviderNamespace, QueryFingerprint = context.Fingerprint,
            PolicyHash = policyHash, ConfirmedAt = properties is null ? null : _clock.GetUtcNow(), Properties = properties ?? []
        };
        snapshots[key] = snapshot;
        person.ProviderProfilesJson = JsonSerializer.Serialize(snapshots);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.Detach(person); return new("concurrent_change", Empty); }
        return new(properties is null ? "unconfirmed" : "confirmed", snapshot.Properties, snapshot.ConfirmedAt);
    }

    public static string ComputePropertyHash(IReadOnlyDictionary<string, JsonElement> properties)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                ProviderProfileContract.NormalizeValue(value).WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    private async Task<string?> LegacyQueryRevisionAsync(CancellationToken ct) => queries is not null &&
        await db.Settings.AsNoTracking().AnyAsync(x => x.Key == SettingKeys.NeutralQueryConfiguration, ct)
        ? (await queries.GetConfigurationAsync(ct)).Revision.ToString("D") : null;

    private static string ComputeHash(JsonElement value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private sealed class ProfileSnapshot
    {
        public Guid AccountId { get; set; }
        public string ProviderNamespace { get; set; } = string.Empty;
        public string StableSubject { get; set; } = string.Empty;
        public string PolicyHash { get; set; } = string.Empty;
        public string? QueryFingerprint { get; set; }
        public string? Hash { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public Dictionary<string, JsonElement> Properties { get; set; } = [];
    }
}
