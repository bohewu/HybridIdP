using System.Security.Cryptography;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class ProviderProfileService(
    IApplicationDbContext db, ProviderProfileClient client, IOptions<ProviderProfileOptions> options,
    TimeProvider? timeProvider = null) : IProviderProfileService, IProviderProfileSnapshotReader
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<(Guid User, string Source), IReadOnlyDictionary<string, JsonElement>> _requestValues = [];
    private static readonly IReadOnlyDictionary<string, JsonElement> Empty = new Dictionary<string, JsonElement>();

    public async Task RefreshAfterLoginAsync(ApplicationUser user, string providerNamespace, string stableSubject,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return;
        foreach (var (name, source) in options.Value.Sources.Where(p => p.Value.ProviderNamespace == providerNamespace))
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

    private async Task<ProviderProfileSnapshotResult> ResolveAsync(ApplicationUser user, string sourceName,
        ProviderProfileSourceOptions source, bool force, string? loginSubject, CancellationToken cancellationToken, bool readOnly = false)
    {
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
        try { snapshots = JsonSerializer.Deserialize<Dictionary<string, ProfileSnapshot>>(person.ProviderProfilesJson ?? "{}") ?? []; }
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
            snapshot.PolicyHash == policyHash;
        if (!readOnly && !force && matching && _requestValues.TryGetValue((user.Id, sourceName), out var requestValues))
            return new("confirmed", requestValues, snapshot!.ConfirmedAt);
        if (!force && matching && snapshot!.ConfirmedAt is { } confirmed && confirmed <= now &&
            options.Value.MaximumAge > TimeSpan.Zero && now - confirmed < options.Value.MaximumAge)
        {
            if (!readOnly) _requestValues[(user.Id, sourceName)] = snapshot.Properties;
            return new("confirmed", snapshot.Properties, confirmed, confirmed + options.Value.MaximumAge);
        }

        if (readOnly)
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
        if (currentLinks.Count != 1 || currentLinks[0].Id != linkIdentity.Id ||
            currentLinks[0].LoginProvider != source.ProviderNamespace || currentLinks[0].ProviderKey != linkIdentity.ProviderKey ||
            requestingAccount is not { IsActive: true, IsDeleted: false } || requestingAccount.PersonId != personId ||
            currentPerson is null || !currentPerson.CanAuthenticate()) return new("account_ineligible", Empty);
        var properties = result?.ExtraProperties.Where(p => source.AllowedProperties.TryGetValue(p.Key, out var type) &&
            ProviderProfileContract.IsValueOfType(p.Value, type))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        snapshot = matching ? snapshot! : new ProfileSnapshot
        {
            AccountId = linkIdentity.Id, ProviderNamespace = source.ProviderNamespace,
            StableSubject = linkIdentity.ProviderKey, PolicyHash = policyHash
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

    private static string ComputeHash(JsonElement value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private sealed class ProfileSnapshot
    {
        public Guid AccountId { get; set; }
        public string ProviderNamespace { get; set; } = string.Empty;
        public string StableSubject { get; set; } = string.Empty;
        public string PolicyHash { get; set; } = string.Empty;
        public string? Hash { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public Dictionary<string, JsonElement> Properties { get; set; } = [];
    }
}
