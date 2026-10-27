using System.Text.Json;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Web.IdP.Services.Saml.Idp;

public sealed class SamlAttributeService(SamlFederationRuntime runtime, ISettingsService settings,
    IApplicationDbContext db)
{
    public bool Enabled => runtime.Options.Idp.Enabled;

    public IReadOnlyList<SamlAttributePartnerDto> Partners() => !Enabled ? [] :
        runtime.Options.Idp.ServiceProviders.Select(p => new SamlAttributePartnerDto(p.EntityId,
            p.AssertionConsumerServiceUrl)).ToArray();

    public void EnsureEnabled()
    {
        if (!Enabled) throw new SamlAttributeException("idp_disabled");
    }

    private void RequirePartner(string? entityId)
    {
        EnsureEnabled();
        if (string.IsNullOrEmpty(entityId) || entityId.Length > 2048)
            throw new SamlAttributeException("invalid_request");
        if (!runtime.Options.Idp.ServiceProviders.Any(p => p.EntityId == entityId))
            throw new SamlAttributeException("unknown_partner");
    }

    public async Task<SamlAttributePolicyDto> GetPolicyAsync(string entityId, CancellationToken ct)
    {
        RequirePartner(entityId);
        var raw = await settings.GetValueAsync(SamlAttributePolicy.SettingKey(entityId), ct);
        if (raw is null) return new(entityId, []);
        try
        {
            var policy = JsonSerializer.Deserialize<SamlAttributePolicyDto>(raw);
            if (policy is null || policy.EntityId != entityId) throw new SamlAttributeException("policy_invalid");
            SamlAttributePolicy.Validate(policy);
            return policy;
        }
        catch (Exception ex) when (ex is JsonException or SamlAttributeException)
        {
            throw new SamlAttributeException("policy_invalid");
        }
    }

    public async Task<SamlAttributePolicyDto> SavePolicyAsync(SamlAttributePolicyDto policy, string? actor, CancellationToken ct)
    {
        RequirePartner(policy.EntityId);
        SamlAttributePolicy.Validate(policy);
        var key = SamlAttributePolicy.SettingKey(policy.EntityId);
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            setting = new Setting { Id = Guid.NewGuid(), Key = key };
            db.Settings.Add(setting);
        }
        setting.Value = JsonSerializer.Serialize(policy);
        setting.UpdatedUtc = DateTime.UtcNow;
        setting.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
        await settings.InvalidateAsync(key);
        await settings.InvalidateAsync(key[..(key.IndexOf('.') + 1)]);
        return policy;
    }

    // Used by both read-only preview and the gated issuance path. No upstream reads or local writes.
    public async Task<SamlAttributePreviewResponse> ResolveAsync(string entityId, Guid userId, CancellationToken ct)
    {
        var policy = await GetPolicyAsync(entityId, ct);
        if (userId == Guid.Empty) throw new SamlAttributeException("invalid_request");
        var user = await db.Users.AsNoTracking().Include(u => u.Person)
            .SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new SamlAttributeException("account_not_found");
        var result = SamlAttributePolicy.Resolve(policy, user);
        return new(entityId, userId, userId.ToString("N"), SamlProfile.PersistentNameIdFormat,
            runtime.Options.Idp.EntityId, entityId, result.Attributes, result.Omitted);
    }

    public async Task<IReadOnlyList<SamlAttributeAccountDto>> AccountsAsync(string? search, int skip, int take, CancellationToken ct)
    {
        EnsureEnabled();
        if (search?.Length > 200) throw new SamlAttributeException("invalid_request");
        var query = db.Users.AsNoTracking().Where(u => !u.IsDeleted);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term)) query = query.Where(u => u.UserName != null && u.UserName.Contains(term));
        return await query.OrderBy(u => u.UserName).ThenBy(u => u.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100))
            .Select(u => new SamlAttributeAccountDto(u.Id, u.UserName ?? u.Id.ToString())).ToListAsync(ct);
    }
}
