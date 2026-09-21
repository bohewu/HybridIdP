using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class NativeRecoveryAssistanceServiceTests
{
    private static readonly NativeRecoveryContext BrowserContext = new("browser-hash", "csrf-hash");

    [Fact]
    public async Task ResendAsync_SupersedesExactChallengeAndPreservesOriginalContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Challenge.TryReserveAttempt(fixture.Time.GetUtcNow(), 5);
        await fixture.Context.SaveChangesAsync();
        var originalHash = fixture.Challenge.CodeHash;

        var result = await fixture.Service.ResendAsync(
            new AdminNativeRecoveryResendRequest(fixture.ActorId, fixture.User.Id));

        Assert.Equal(RecoveryProofOutcome.Success, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync();
        var email = await fixture.Context.RecoveryEmails.SingleAsync();
        Assert.Equal(0, challenge.VerificationAttempts);
        Assert.NotEqual(originalHash, challenge.CodeHash);
        Assert.Equal(BrowserContext.ContextHash, challenge.NativeContextHash);
        Assert.Equal(BrowserContext.CsrfHash, challenge.NativeCsrfHash);
        Assert.Equal(email.Version, challenge.NativeRecoveryEmailVersion);
        Assert.Equal("recovery@example.test", fixture.Email.LastTo);
    }

    [Fact]
    public async Task ReplaceAndVerifyEmail_UsesOriginalContextAndInvalidatesPriorState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var approval = fixture.CreateApproval();
        fixture.Context.NativeRecoveryResetApprovals.Add(approval);
        await fixture.Context.SaveChangesAsync();

        var replaced = await fixture.Service.ReplaceEmailAsync(
            new AdminNativeRecoveryEmailReplacementRequest(
                fixture.ActorId,
                fixture.User.Id,
                "new-recovery@example.test",
                "checked employee record",
                "address no longer accessible"));

        Assert.Equal(RecoveryProofOutcome.Success, replaced.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var email = await fixture.Context.RecoveryEmails.SingleAsync();
        var oldChallenge = await fixture.Context.RecoveryProofChallenges.SingleAsync(
            candidate => candidate.Id == fixture.Challenge.Id);
        var verification = await fixture.Context.RecoveryProofChallenges.SingleAsync(
            candidate => candidate.NativeRecoveryChallengeId == fixture.Challenge.Id);
        var storedApproval = await fixture.Context.NativeRecoveryResetApprovals.SingleAsync();
        Assert.Null(email.VerifiedAtUtc);
        Assert.Equal("new-recovery@example.test", email.Address);
        Assert.NotNull(oldChallenge.RevokedAtUtc);
        Assert.NotNull(storedApproval.RevokedAtUtc);
        Assert.Equal(email.Version, verification.NativeRecoveryEmailVersion);

        var verified = await fixture.Service.VerifyReplacementAsync(
            new NativeRecoveryReplacementVerificationRequest(
                fixture.Challenge.Id,
                fixture.Email.LastCode!,
                BrowserContext));

        Assert.Equal(RecoveryProofOutcome.Success, verified);
        fixture.Context.ChangeTracker.Clear();
        Assert.NotNull((await fixture.Context.RecoveryEmails.SingleAsync()).VerifiedAtUtc);
    }

    [Fact]
    public async Task VerifyReplacementAsync_DeniesDifferentBrowserContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ReplaceEmailAsync(
            new AdminNativeRecoveryEmailReplacementRequest(
                fixture.ActorId,
                fixture.User.Id,
                "new-recovery@example.test",
                "checked employee record",
                "address no longer accessible"));

        var outcome = await fixture.Service.VerifyReplacementAsync(
            new NativeRecoveryReplacementVerificationRequest(
                fixture.Challenge.Id,
                fixture.Email.LastCode!,
                BrowserContext with { CsrfHash = "different" }));

        Assert.Equal(RecoveryProofOutcome.Unavailable, outcome);
        fixture.Context.ChangeTracker.Clear();
        Assert.Null((await fixture.Context.RecoveryEmails.SingleAsync()).VerifiedAtUtc);
    }

    [Fact]
    public async Task ApproveResetAsync_CreatesBoundApprovalWithoutTransferableToken()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ApproveResetAsync(
            new AdminNativeRecoveryApprovalRequest(
                fixture.ActorId,
                fixture.User.Id,
                "checked employee record",
                "manual reset requested"));

        Assert.Equal(RecoveryProofOutcome.Success, result.Outcome);
        var approval = await fixture.Context.NativeRecoveryResetApprovals.SingleAsync();
        Assert.Equal(fixture.Challenge.Id, approval.RecoveryProofChallengeId);
        Assert.Equal(BrowserContext.ContextHash, approval.ContextHash);
        Assert.Equal(BrowserContext.CsrfHash, approval.CsrfHash);
        Assert.Equal(fixture.User.SecurityStamp, approval.SecurityStamp);
        Assert.True(approval.ExpiresAtUtc <= fixture.Challenge.ExpiresAtUtc);
        Assert.DoesNotContain("Token", typeof(NativeRecoveryResetApproval).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task Actions_DenyWhenFeatureOffUnauthorizedOrAccountLocked()
    {
        await using var disabled = await Fixture.CreateAsync(enabled: false);
        Assert.Equal(
            RecoveryProofOutcome.Unavailable,
            (await disabled.Service.ResendAsync(
                new AdminNativeRecoveryResendRequest(disabled.ActorId, disabled.User.Id))).Outcome);

        await using var unauthorized = await Fixture.CreateAsync(authorized: false);
        Assert.Equal(
            RecoveryProofOutcome.Unauthorized,
            (await unauthorized.Service.ApproveResetAsync(
                new AdminNativeRecoveryApprovalRequest(
                    unauthorized.ActorId,
                    unauthorized.User.Id,
                    "identity evidence",
                    "support reason"))).Outcome);

        await using var locked = await Fixture.CreateAsync();
        locked.User.LockoutEnabled = true;
        locked.User.LockoutEnd = locked.Time.GetUtcNow().AddMinutes(5);
        await locked.Context.SaveChangesAsync();
        Assert.Equal(
            RecoveryProofOutcome.Unavailable,
            (await locked.Service.ResendAsync(
                new AdminNativeRecoveryResendRequest(locked.ActorId, locked.User.Id))).Outcome);
        Assert.Empty(locked.Email.Messages);
    }

    [Fact]
    public async Task ResendAsync_AllowsCompletedDirectoryAuthorityWithoutChangingPasswordHash()
    {
        await using var fixture = await Fixture.CreateAsync(directoryAuthority: true);
        var originalHash = fixture.User.PasswordHash;

        var result = await fixture.Service.ResendAsync(
            new AdminNativeRecoveryResendRequest(fixture.ActorId, fixture.User.Id));

        Assert.Equal(RecoveryProofOutcome.Success, result.Outcome);
        fixture.Context.ChangeTracker.Clear();
        var user = await fixture.Context.Users.SingleAsync();
        var challenge = await fixture.Context.RecoveryProofChallenges.SingleAsync();
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.True(challenge.NativeDirectoryAuthority);
        Assert.NotNull(challenge.NativeDirectoryObjectId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            ApplicationDbContext context,
            ApplicationUser user,
            RecoveryEmailRecord recoveryEmail,
            RecoveryProofChallenge challenge,
            Guid actorId,
            FixedTimeProvider time,
            CapturingEmailService email,
            NativeRecoveryAssistanceService service)
        {
            _connection = connection;
            Context = context;
            User = user;
            RecoveryEmail = recoveryEmail;
            Challenge = challenge;
            ActorId = actorId;
            Time = time;
            Email = email;
            Service = service;
        }

        public ApplicationDbContext Context { get; }
        public ApplicationUser User { get; }
        public RecoveryEmailRecord RecoveryEmail { get; }
        public RecoveryProofChallenge Challenge { get; }
        public Guid ActorId { get; }
        public FixedTimeProvider Time { get; }
        public CapturingEmailService Email { get; }
        public NativeRecoveryAssistanceService Service { get; }

        public static async Task<Fixture> CreateAsync(
            bool enabled = true,
            bool authorized = true,
            bool directoryAuthority = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var time = new FixedTimeProvider();
            var hasher = new PasswordHasher<ApplicationUser>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "ordinary-user",
                NormalizedUserName = "ORDINARY-USER",
                Email = "user@example.test",
                NormalizedEmail = "USER@EXAMPLE.TEST",
                IsActive = true,
                SecurityStamp = "current-security-stamp"
            };
            user.PasswordHash = hasher.HashPassword(user, "Current!Password1");
            context.Users.Add(user);

            Guid? directoryObjectId = null;
            if (directoryAuthority)
            {
                var binding = new ProviderSubjectDirectoryBinding(
                    user.Id,
                    "directory",
                    "subject",
                    Guid.NewGuid(),
                    time.GetUtcNow().UtcDateTime,
                    user.UserName);
                var migration = new CredentialMigrationStateRecord(user.Id, binding.Id, time.GetUtcNow());
                migration.Advance(
                    CredentialMigrationState.ProofValidated,
                    EffectiveEmailOtpRequirement.NotRequired,
                    time.GetUtcNow());
                migration.Advance(CredentialMigrationState.DirectoryCredentialCommitted, time.GetUtcNow());
                migration.Advance(CredentialMigrationState.LocalFinalized, time.GetUtcNow());
                directoryObjectId = binding.DirectoryObjectId;
                context.ProviderSubjectDirectoryBindings.Add(binding);
                context.CredentialMigrationStateRecords.Add(migration);
            }

            var recoveryEmail = new RecoveryEmailRecord(
                user.Id,
                "recovery@example.test",
                "RECOVERY@EXAMPLE.TEST",
                time.GetUtcNow().AddHours(-1));
            recoveryEmail.MarkVerified(time.GetUtcNow().AddMinutes(-30));
            context.RecoveryEmails.Add(recoveryEmail);
            await context.SaveChangesAsync();
            var originalCode = "123456";
            var codeHash = hasher.HashPassword(
                user,
                RecoveryProofSecurity.BindToContext(
                    originalCode,
                    BrowserContext.ContextHash,
                    BrowserContext.CsrfHash,
                    CreateEmailBinding(recoveryEmail)));
            var challenge = new RecoveryProofChallenge(
                recoveryEmail.Id,
                user.Id,
                RecoveryProofPurpose.NativePasswordRecovery,
                codeHash,
                time.GetUtcNow(),
                time.GetUtcNow().AddMinutes(10));
            challenge.BindNativeAssistance(
                BrowserContext.ContextHash,
                BrowserContext.CsrfHash,
                directoryAuthority,
                directoryObjectId,
                recoveryEmail.Version,
                user.SecurityStamp);
            context.RecoveryProofChallenges.Add(challenge);
            context.SecurityPolicies.Add(new SecurityPolicy { ForgotPasswordMode = ForgotPasswordMode.Native });
            await context.SaveChangesAsync();

            var options = Options.Create(new ForgotPasswordRecoveryOptions
            {
                DeploymentCeiling = ForgotPasswordMode.Native,
                NativeRecoveryEnabled = true,
                NativeDirectoryRecoveryEnabled = true,
                OrdinaryRecoveryAssistanceEnabled = enabled
            });
            var authorizer = new Mock<IRecoveryProofAuthorizer>();
            authorizer.Setup(service => service.IsAdministratorAuthorizedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(authorized);
            var audit = new Mock<IRecoveryProofAudit>();
            audit.Setup(service => service.RecordAsync(
                    It.IsAny<RecoveryProofAuditEvent>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var email = new CapturingEmailService();
            var service = new NativeRecoveryAssistanceService(
                context,
                authorizer.Object,
                new FixedPolicyEvaluator(() => context.RecoveryEmails.AsNoTracking().Single().Address),
                new ForgotPasswordRoutingEvaluator(options),
                email,
                hasher,
                audit.Object,
                options,
                time);
            return new Fixture(connection, context, user, recoveryEmail, challenge, Guid.NewGuid(), time, email, service);
        }

        public NativeRecoveryResetApproval CreateApproval() =>
            new(
                Challenge.Id,
                User.Id,
                RecoveryEmail.Id,
                RecoveryEmail.Version,
                ActorId,
                BrowserContext.ContextHash,
                BrowserContext.CsrfHash,
                Challenge.NativeDirectoryAuthority == true,
                Challenge.NativeDirectoryObjectId,
                User.SecurityStamp!,
                "manual reset",
                "identity evidence",
                Time.GetUtcNow(),
                Time.GetUtcNow().AddMinutes(5));

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedPolicyEvaluator(Func<string> address) : IRecoveryVerificationPolicyEvaluator
    {
        public Task<RecoveryVerificationPolicyDecision> EvaluateAsync(
            Guid localAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryVerificationPolicyDecision(
                true,
                "period",
                true,
                true,
                false,
                false,
                RecoveryPeriodDisposition.Satisfied,
                new RecoveryEmailPolicyDecision(
                    address(),
                    RecoveryEmailAddressSource.LocalRecord,
                    RecoveryEmailPolicyTrustOrigin.LocallyVerified,
                    true,
                    false,
                    false)));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 11, 4, 0, 0, TimeSpan.Zero);
    }

    private sealed class CapturingEmailService : IEmailService
    {
        public List<(string To, string Body)> Messages { get; } = [];
        public string? LastTo => Messages.LastOrDefault().To;
        public string? LastCode
        {
            get
            {
                var body = Messages.LastOrDefault().Body;
                if (body is null)
                {
                    return null;
                }
                var match = System.Text.RegularExpressions.Regex.Match(body, @"\b\d{6}\b");
                return match.Success ? match.Value : null;
            }
        }

        public Task SendEmailAsync(
            string to,
            string subject,
            string body,
            bool isHtml = true,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((to, body));
            return Task.CompletedTask;
        }

        public Task SendTestEmailAsync(
            MailSettingsDto settings,
            string to,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static string CreateEmailBinding(RecoveryEmailRecord email) =>
        $"{email.Id:N}:{email.LocalAccountId:N}:{email.Version}:{email.NormalizedAddress}";
}
