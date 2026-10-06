using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Web.IdP.Services;

/// <summary>Fresh local and, when required, provider eligibility immediately before an action.</summary>
public sealed class CurrentUserLifecycleEligibility : ICurrentUserLifecycleEligibility
{
    private readonly IApplicationDbContext _context;
    private readonly IOptions<ProviderLifecycleOptions> _options;
    private readonly IProviderLifecycleClient? _client;
    private readonly ProviderLifecycleClock _clock;

    // Compatibility for existing explicitly local-only callers. Production DI uses the full constructor.
    public CurrentUserLifecycleEligibility(IApplicationDbContext context)
        : this(context, Microsoft.Extensions.Options.Options.Create(new ProviderLifecycleOptions()), null,
            new ProviderLifecycleClock(TimeProvider.System)) { }

    public CurrentUserLifecycleEligibility(IApplicationDbContext context,
        IOptions<ProviderLifecycleOptions> options, IProviderLifecycleClient? client,
        ProviderLifecycleClock clock)
    {
        _context = context;
        _options = options;
        _client = client;
        _clock = clock;
    }

    public async Task<bool> IsEligibleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty) return false;
        try
        {
            // Copy mutable options before any await; approvals and transport policy cannot drift in flight.
            var policy = ReadPolicy();
            var account = policy.Enabled
                ? policy.RequiredAccounts.SingleOrDefault(candidate => candidate.LocalAccountId == userId)
                : null;
            var initial = await ReadLocalAsync(userId, cancellationToken);
            if (account is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return policy.Matches(ReadPolicy()) && IsLocallyEligible(initial, DateTimeOffset.UtcNow);
            }

            if (!_clock.TryGetUtcNow(out var startedAt) || !IsLocallyEligible(initial, startedAt)) return false;
            var binding = await ReadBindingAsync(userId, account, cancellationToken);
            if (binding is null || _client is null) return false;
            var lookup = await _client.LookupAsync(binding.Tuple, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Failure=None means the strict client already verified requestId and the whole wire exchange.
            var response = lookup.Response;
            if (lookup.Failure != ProviderLifecycleFailure.None || response is null ||
                response.Outcome != ProviderLifecycleOutcome.Found ||
                response.AccountState != ProviderLifecycleAccountState.Enabled ||
                !ProviderLifecycleContract.IsRequestId(response.RequestId) ||
                response.Binding != binding.Tuple || response.Evidence is not { } evidence ||
                !string.Equals(evidence.SourceAuthority, account.SourceAuthority, StringComparison.Ordinal) ||
                !string.Equals(evidence.MappingVersion, account.MappingVersion, StringComparison.Ordinal)) return false;

            var currentBinding = await ReadBindingAsync(userId, account, cancellationToken);
            var current = await ReadLocalAsync(userId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (binding != currentBinding || current is null || initial is null ||
                current.PersonId != initial.PersonId ||
                !string.Equals(current.SecurityStamp, initial.SecurityStamp, StringComparison.Ordinal) ||
                !string.Equals(current.ConcurrencyStamp, initial.ConcurrencyStamp, StringComparison.Ordinal) ||
                !policy.Matches(ReadPolicy()) || !_clock.TryGetUtcNow(out var actionTime)) return false;

            return IsLocallyEligible(current, actionTime) &&
                evidence.EffectiveFrom < evidence.EffectiveUntil &&
                evidence.ObservedAt <= actionTime &&
                actionTime - evidence.ObservedAt <= TimeSpan.FromSeconds(policy.MaxAgeSeconds) &&
                evidence.EffectiveFrom <= actionTime && actionTime < evidence.EffectiveUntil;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    private Task<LocalAccountSnapshot?> ReadLocalAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new LocalAccountSnapshot(user.IsActive, user.IsDeleted, user.LockoutEnd,
                user.PersonId, user.SecurityStamp, user.ConcurrencyStamp,
                user.Person == null ? null : new Person
                {
                    IsDeleted = user.Person.IsDeleted,
                    Status = user.Person.Status,
                    StartDate = user.Person.StartDate,
                    EndDate = user.Person.EndDate
                }))
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsLocallyEligible(LocalAccountSnapshot? user, DateTimeOffset now) =>
        user is { IsActive: true, IsDeleted: false } &&
        (!user.LockoutEnd.HasValue || user.LockoutEnd <= now) &&
        (!user.PersonId.HasValue || user.Person?.CanAuthenticate(now.UtcDateTime) == true);

    private async Task<BindingSnapshot?> ReadBindingAsync(Guid userId, AccountPolicy account,
        CancellationToken cancellationToken)
    {
        if (account.BindingKind == ProviderLifecycleRequiredAccount.DirectoryBinding)
        {
            // Filter strings in memory: database collation must not choose the identity.
            var rows = await _context.ProviderSubjectDirectoryBindings.AsNoTracking()
                .Where(row => row.LocalAccountId == userId).ToListAsync(cancellationToken);
            var matches = rows.Where(row => string.Equals(row.ProviderNamespace, account.ProviderNamespace,
                StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) return null;
            var row = matches[0];
            var tuple = new ProviderLifecycleBinding
                { ProviderNamespace = row.ProviderNamespace, StableSubject = row.StableSubject };
            return tuple.IsValid() ? new BindingSnapshot(tuple, row.Id, row.DirectoryObjectId,
                row.CreatedAtUtc, row.NormalizedCanonicalAccountAlias, null) : null;
        }

        var logins = await _context.UserLogins.AsNoTracking()
            .Where(row => row.UserId == userId).ToListAsync(cancellationToken);
        var selected = logins.Where(row => string.Equals(row.LoginProvider, account.ExternalLoginProvider,
            StringComparison.Ordinal)).ToArray();
        if (selected.Length != 1) return null;
        var login = selected[0];
        // The explicitly approved namespace maps this stored LoginProvider/key pair; never infer it.
        var externalTuple = new ProviderLifecycleBinding
            { ProviderNamespace = account.ProviderNamespace, StableSubject = login.ProviderKey };
        return externalTuple.IsValid()
            ? new BindingSnapshot(externalTuple, null, null, null, null, login.LoginProvider) : null;
    }

    private PolicySnapshot ReadPolicy()
    {
        var value = _options.Value;
        if (!value.Enabled) return new PolicySnapshot(false, null, null, 0, 0, []);
        var copy = new ProviderLifecycleOptions
        {
            Enabled = value.Enabled, Endpoint = value.Endpoint, SharedSecret = value.SharedSecret,
            TimeoutSeconds = value.TimeoutSeconds, MaxAgeSeconds = value.MaxAgeSeconds,
            RequiredAccounts = value.RequiredAccounts?.Select(account => new ProviderLifecycleRequiredAccount
            {
                LocalAccountId = account.LocalAccountId, BindingKind = account.BindingKind,
                ProviderNamespace = account.ProviderNamespace, ExternalLoginProvider = account.ExternalLoginProvider,
                SourceAuthority = account.SourceAuthority, MappingVersion = account.MappingVersion
            }).ToList() ?? []
        };
        if (new ProviderLifecycleOptionsValidator().Validate(null, copy).Failed)
            throw new InvalidOperationException("Lifecycle policy is invalid.");
        return new PolicySnapshot(copy.Enabled, copy.Endpoint, copy.SharedSecret, copy.TimeoutSeconds,
            copy.MaxAgeSeconds, copy.RequiredAccounts.Select(account => new AccountPolicy(account.LocalAccountId,
                account.BindingKind, account.ProviderNamespace, account.ExternalLoginProvider,
                account.SourceAuthority, account.MappingVersion)).ToArray());
    }

    private sealed record LocalAccountSnapshot(bool IsActive, bool IsDeleted, DateTimeOffset? LockoutEnd,
        Guid? PersonId, string? SecurityStamp, string? ConcurrencyStamp, Person? Person);

    // Record identity / stamps detect local changes only; they never stand in for MappingVersion.
    private sealed record BindingSnapshot(ProviderLifecycleBinding Tuple, Guid? RecordId, Guid? DirectoryObjectId,
        DateTime? CreatedAtUtc, string? CanonicalAlias, string? LoginProvider)
    {
        public override string ToString() => "BindingSnapshot [redacted]";
    }

    private sealed record AccountPolicy(Guid LocalAccountId, string BindingKind, string ProviderNamespace,
        string? ExternalLoginProvider, string SourceAuthority, string MappingVersion)
    {
        public override string ToString() => "AccountPolicy [redacted]";
    }

    private sealed record PolicySnapshot(bool Enabled, string? Endpoint, string? SharedSecret,
        int TimeoutSeconds, int MaxAgeSeconds, AccountPolicy[] RequiredAccounts)
    {
        public bool Matches(PolicySnapshot other) => Enabled == other.Enabled && Endpoint == other.Endpoint &&
            SharedSecret == other.SharedSecret && TimeoutSeconds == other.TimeoutSeconds &&
            MaxAgeSeconds == other.MaxAgeSeconds && RequiredAccounts.SequenceEqual(other.RequiredAccounts);
        public override string ToString() => "PolicySnapshot [redacted]";
    }
}
