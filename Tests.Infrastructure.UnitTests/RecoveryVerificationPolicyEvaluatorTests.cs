using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryVerificationPolicyEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_DefaultDisabled_DoesNotAccessDurableEvidence()
    {
        var context = new Mock<IApplicationDbContext>(MockBehavior.Strict);
        var evaluator = new RecoveryVerificationPolicyEvaluator(
            context.Object,
            Options.Create(new RecoveryVerificationPolicyOptions()));

        var decision = await evaluator.EvaluateAsync(Guid.NewGuid());

        Assert.False(decision.Enabled);
        Assert.Equal(RecoveryPeriodDisposition.Disabled, decision.PeriodDisposition);
        Assert.False(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        context.VerifyNoOtherCalls();
    }

    [Fact]
    public void OptionsValidator_RequiresOneValidGlobalPeriodAndFixedBootstrapCutoff()
    {
        var validator = new RecoveryVerificationPolicyOptionsValidator();
        var invalid = new RecoveryVerificationPolicyOptions
        {
            Enabled = true,
            CurrentPeriodId = "period-a",
            EffectiveAtUtc = At("2026-09-10T00:00:00Z"),
            GraceEndsAtUtc = At("2026-09-09T00:00:00Z"),
            BootstrapEnabled = true
        };

        var invalidResult = validator.Validate(null, invalid);
        var validResult = validator.Validate(null, EnabledOptions());

        Assert.False(invalidResult.Succeeded);
        Assert.Contains(invalidResult.Failures, failure => failure.Contains("grace deadline", StringComparison.Ordinal));
        Assert.Contains(invalidResult.Failures, failure => failure.Contains("fixed UTC cutoff", StringComparison.Ordinal));
        Assert.True(validResult.Succeeded);
        Assert.False(new RecoveryVerificationPolicyOptions().AcceptSourceVerifiedEmails);
        Assert.False(new RecoveryVerificationPolicyOptions().AcceptPolicyTrustedEmails);
    }

    [Fact]
    public async Task EvaluateAsync_TrustedProviderEmail_StillRequiresCurrentPeriodVerification()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(context, accountId, "source@example.test", ProviderEmailTrustOrigin.SourceVerified);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.True(decision.RequiresCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Required, decision.PeriodDisposition);
        Assert.True(decision.RecoveryEmail.CanReceiveRecoveryOtp);
    }

    [Fact]
    public async Task EvaluateAsync_NoDurableEvidence_RequiresVerificationAndRoutesRecoveryToAssistance()
    {
        await using var context = CreateContext();

        var decision = await CreateEvaluator(context).EvaluateAsync(Guid.NewGuid());

        Assert.True(decision.RequiresCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Required, decision.PeriodDisposition);
        Assert.False(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.Null(decision.RecoveryEmail.Address);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.Unknown, decision.RecoveryEmail.TrustOrigin);
    }

    [Theory]
    [InlineData(ProviderMetadataEvidenceState.Missing)]
    [InlineData(ProviderMetadataEvidenceState.Malformed)]
    [InlineData(ProviderMetadataEvidenceState.Unsupported)]
    [InlineData(ProviderMetadataEvidenceState.Stale)]
    [InlineData(ProviderMetadataEvidenceState.Untrusted)]
    [InlineData(ProviderMetadataEvidenceState.AuthenticationFailed)]
    [InlineData(ProviderMetadataEvidenceState.TimedOut)]
    [InlineData(ProviderMetadataEvidenceState.Unavailable)]
    public async Task EvaluateAsync_NegativeMetadata_CannotAuthorizeButPreservesLocalVerification(
        ProviderMetadataEvidenceState state)
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        var snapshot = SeedSnapshot(context, accountId, "source@example.test", ProviderEmailTrustOrigin.SourceVerified);
        snapshot.Invalidate(At("2026-09-09T00:00:00Z"), state);
        await context.SaveChangesAsync();

        var denied = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal(state, Assert.Single(denied.MetadataEvidenceStates));
        Assert.True(denied.RequiresCurrentPeriodVerification);
        Assert.False(denied.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.False(denied.BootstrapActive);
        var local = new RecoveryEmailRecord(accountId, "local@example.test", "LOCAL@EXAMPLE.TEST", At("2026-09-09T00:00:00Z"));
        local.MarkVerified(At("2026-09-09T00:01:00Z"));
        context.RecoveryEmails.Add(local);
        await context.SaveChangesAsync();

        var allowed = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal(RecoveryPeriodDisposition.Satisfied, allowed.PeriodDisposition);
        Assert.True(allowed.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.LocallyVerified, allowed.RecoveryEmail.TrustOrigin);
    }

    [Theory]
    [InlineData("2026-08-31T23:59:59Z", ProviderMetadataEvidenceState.Stale)]
    [InlineData("2026-09-11T00:00:00Z", ProviderMetadataEvidenceState.Untrusted)]
    public async Task EvaluateAsync_OutsideCurrentPeriodEvidence_CannotBootstrap(
        string refreshedAt, ProviderMetadataEvidenceState state)
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        var snapshot = SeedSnapshot(context, accountId);
        snapshot.Refresh("source@example.test", ProviderEmailTrustOrigin.SourceVerified, null, At(refreshedAt));
        await context.SaveChangesAsync();
        var options = EnabledOptions();
        options.BootstrapEnabled = true;
        options.BootstrapUntilUtc = At("2026-09-20T00:00:00Z");

        var decision = await CreateEvaluator(context, options).EvaluateAsync(accountId);

        Assert.Equal(state, Assert.Single(decision.MetadataEvidenceStates));
        Assert.False(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.False(decision.BootstrapActive);
        Assert.Equal(RecoveryPeriodDisposition.Required, decision.PeriodDisposition);
    }

    [Fact]
    public async Task EvaluateAsync_LegacySerializedSnapshot_CannotRestoreAnyAuthority()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        var current = SeedSnapshot(context, accountId);
        context.ProviderMetadataSnapshots.Remove(current);
        // A cached entity is not a trusted deserialization boundary. Its private
        // setters and constructor retain Missing even when extras claim authority.
        var json = JsonSerializer.Serialize(new
        {
            ProviderSubjectDirectoryBindingId = current.ProviderSubjectDirectoryBindingId,
            RefreshedAtUtc = At("2026-09-09T00:00:00Z"),
            Email = "source@example.test",
            EmailTrustOrigin = 1,
            EvidenceState = 1,
            legacyCategories = new[] { "synthetic-category" },
            legacyStatus = "Complete",
            legacySourceTimestamp = At("2026-09-09T00:00:00Z"),
            extensionData = new { legacyCategories = new[] { "synthetic-category" } },
            renamedCategories = new[] { "synthetic-category" }
        });
        var restored = JsonSerializer.Deserialize<ProviderMetadataSnapshot>(json)!;
        context.ProviderMetadataSnapshots.Add(restored);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal(ProviderMetadataEvidenceState.Missing, restored.EvidenceState);
        Assert.Null(restored.Email);
        Assert.True(decision.RequiresCurrentPeriodVerification);
        Assert.False(decision.RecoveryEmail.CanReceiveRecoveryOtp);
    }

    [Fact]
    public async Task EvaluateAsync_SourceTimestampsNeverSatisfyCurrentPeriod()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(
            context,
            accountId,
            "person@example.test",
            ProviderEmailTrustOrigin.SourceVerified,
            At("2026-09-09T00:00:00Z"));
        var local = new RecoveryEmailRecord(accountId, "person@example.test", "PERSON@EXAMPLE.TEST", At("2026-08-01T00:00:00Z"));
        local.MarkVerified(At("2026-08-01T00:01:00Z"));
        context.RecoveryEmails.Add(local);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.False(decision.HasCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Required, decision.PeriodDisposition);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.LocallyVerified, decision.RecoveryEmail.TrustOrigin);
        Assert.True(decision.RecoveryEmail.CanReceiveRecoveryOtp);
    }

    [Fact]
    public async Task EvaluateAsync_CurrentLocalVerification_SatisfiesPeriod()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(context, accountId);
        var local = new RecoveryEmailRecord(accountId, "person@example.test", "PERSON@EXAMPLE.TEST", At("2026-09-01T00:00:00Z"));
        local.MarkVerified(At("2026-09-02T00:00:00Z"));
        context.RecoveryEmails.Add(local);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.True(decision.HasCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Satisfied, decision.PeriodDisposition);
    }

    [Fact]
    public async Task EvaluateAsync_SourceConflict_PreservesIndependentLocallyVerifiedAddress()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(
            context,
            accountId,
            "source@example.test",
            ProviderEmailTrustOrigin.SourceVerified);
        var local = new RecoveryEmailRecord(accountId, "local@example.test", "LOCAL@EXAMPLE.TEST", At("2026-08-01T00:00:00Z"));
        local.MarkVerified(At("2026-08-01T00:01:00Z"));
        context.RecoveryEmails.Add(local);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal("local@example.test", decision.RecoveryEmail.Address);
        Assert.Equal(RecoveryEmailAddressSource.LocalRecord, decision.RecoveryEmail.AddressSource);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.LocallyVerified, decision.RecoveryEmail.TrustOrigin);
        Assert.True(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.True(decision.RecoveryEmail.HasSourceConflict);
        Assert.False(decision.RecoveryEmail.HasAcceptedSourceTrustForAddress);
    }

    [Fact]
    public async Task EvaluateAsync_UnverifiedLocalConflict_DoesNotSelectDifferentSourceAddress()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(
            context,
            accountId,
            "source@example.test",
            ProviderEmailTrustOrigin.SourceVerified);
        context.RecoveryEmails.Add(new RecoveryEmailRecord(
            accountId,
            "pending@example.test",
            "PENDING@EXAMPLE.TEST",
            At("2026-09-01T00:00:00Z")));
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal("pending@example.test", decision.RecoveryEmail.Address);
        Assert.Equal(RecoveryEmailAddressSource.LocalRecord, decision.RecoveryEmail.AddressSource);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.Unknown, decision.RecoveryEmail.TrustOrigin);
        Assert.False(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.True(decision.RecoveryEmail.HasSourceConflict);
    }

    [Fact]
    public async Task EvaluateAsync_NoLocalRecord_ReturnsSingleAcceptedSourceAsNonPersistedCandidate()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(
            context,
            accountId,
            "source@example.test",
            ProviderEmailTrustOrigin.SourceVerified);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal("source@example.test", decision.RecoveryEmail.Address);
        Assert.Equal(RecoveryEmailAddressSource.ProviderSnapshot, decision.RecoveryEmail.AddressSource);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.SourceVerified, decision.RecoveryEmail.TrustOrigin);
        Assert.True(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.Empty(context.RecoveryEmails);
    }

    [Fact]
    public async Task EvaluateAsync_SourceBootstrapRevoked_DoesNotReturnSourceOnlyCandidate()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = accountId,
            UserName = "revoked-account",
            RecoverySourceBootstrapRevokedAtUtc = At("2026-09-09T00:00:00Z")
        });
        SeedSnapshot(
            context,
            accountId,
            "source@example.test",
            ProviderEmailTrustOrigin.SourceVerified);
        await context.SaveChangesAsync();

        var decision = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Null(decision.RecoveryEmail.Address);
        Assert.Equal(RecoveryEmailAddressSource.None, decision.RecoveryEmail.AddressSource);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.Unknown, decision.RecoveryEmail.TrustOrigin);
        Assert.False(decision.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.False(decision.RecoveryEmail.HasAcceptedSourceTrustForAddress);
    }

    [Fact]
    public async Task EvaluateAsync_SourceBootstrapRevoked_RequiresIndependentVerificationForReEnrollment()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        context.Users.Add(new ApplicationUser
        {
            Id = accountId,
            UserName = "re-enrolled-account",
            RecoverySourceBootstrapRevokedAtUtc = At("2026-09-09T00:00:00Z")
        });
        SeedSnapshot(
            context,
            accountId,
            "source@example.test",
            ProviderEmailTrustOrigin.SourceVerified);
        var local = new RecoveryEmailRecord(
            accountId,
            "source@example.test",
            "SOURCE@EXAMPLE.TEST",
            At("2026-09-09T01:00:00Z"));
        context.RecoveryEmails.Add(local);
        await context.SaveChangesAsync();

        var pending = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal(RecoveryEmailAddressSource.LocalRecord, pending.RecoveryEmail.AddressSource);
        Assert.Equal(RecoveryEmailPolicyTrustOrigin.Unknown, pending.RecoveryEmail.TrustOrigin);
        Assert.False(pending.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.False(pending.RecoveryEmail.HasAcceptedSourceTrustForAddress);

        local.MarkVerified(At("2026-09-10T01:00:00Z"));
        await context.SaveChangesAsync();
        var verified = await CreateEvaluator(context).EvaluateAsync(accountId);

        Assert.Equal(RecoveryEmailPolicyTrustOrigin.LocallyVerified, verified.RecoveryEmail.TrustOrigin);
        Assert.True(verified.RecoveryEmail.CanReceiveRecoveryOtp);
        Assert.True(verified.HasCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.Satisfied, verified.PeriodDisposition);
        Assert.NotNull((await context.Users.AsNoTracking().SingleAsync(user => user.Id == accountId))
            .RecoverySourceBootstrapRevokedAtUtc);
    }

    [Fact]
    public async Task EvaluateAsync_BootstrapUsesFixedCutoffAndNeverClaimsPeriodSatisfied()
    {
        await using var context = CreateContext();
        var accountId = Guid.NewGuid();
        SeedSnapshot(
            context,
            accountId,
            "source@example.test",
            ProviderEmailTrustOrigin.PolicyTrusted);
        await context.SaveChangesAsync();
        var options = EnabledOptions();
        options.AcceptPolicyTrustedEmails = true;
        options.BootstrapEnabled = true;
        options.BootstrapUntilUtc = At("2026-09-20T00:00:00Z");

        var beforeCutoff = await CreateEvaluator(context, options, At("2026-09-15T00:00:00Z"))
            .EvaluateAsync(accountId);
        var afterCutoff = await CreateEvaluator(context, options, At("2026-09-21T00:00:00Z"))
            .EvaluateAsync(accountId);

        Assert.True(beforeCutoff.BootstrapActive);
        Assert.False(beforeCutoff.HasCurrentPeriodVerification);
        Assert.Equal(RecoveryPeriodDisposition.DeferredByBootstrap, beforeCutoff.PeriodDisposition);
        Assert.False(afterCutoff.BootstrapActive);
        Assert.Equal(RecoveryPeriodDisposition.Required, afterCutoff.PeriodDisposition);
    }

    private static RecoveryVerificationPolicyEvaluator CreateEvaluator(
        IApplicationDbContext context,
        RecoveryVerificationPolicyOptions? options = null,
        DateTimeOffset? now = null) =>
        new(
            context,
            Options.Create(options ?? EnabledOptions()),
            new FixedTimeProvider(now ?? At("2026-09-10T00:00:00Z")));

    private static RecoveryVerificationPolicyOptions EnabledOptions() => new()
    {
        Enabled = true,
        CurrentPeriodId = "period-a",
        EffectiveAtUtc = At("2026-09-01T00:00:00Z"),
        GraceEndsAtUtc = At("2026-09-05T00:00:00Z"),
        AcceptSourceVerifiedEmails = true
    };

    private static ProviderMetadataSnapshot SeedSnapshot(
        ApplicationDbContext context,
        Guid accountId,
        string? email = null,
        ProviderEmailTrustOrigin emailTrustOrigin = ProviderEmailTrustOrigin.Unknown,
        DateTimeOffset? sourceVerifiedAt = null)
    {
        var binding = new ProviderSubjectDirectoryBinding(
            accountId,
            $"provider-{Guid.NewGuid():N}",
            $"subject-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            At("2026-08-01T00:00:00Z").UtcDateTime);
        var snapshot = new ProviderMetadataSnapshot(binding.Id, At("2026-09-09T00:00:00Z"));
        snapshot.Refresh(
            email,
            emailTrustOrigin,
            sourceVerifiedAt,
            At("2026-09-09T00:01:00Z"));
        context.ProviderSubjectDirectoryBindings.Add(binding);
        context.ProviderMetadataSnapshots.Add(snapshot);
        return snapshot;
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
