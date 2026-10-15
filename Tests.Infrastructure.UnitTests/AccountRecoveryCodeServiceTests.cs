using System.Formats.Cbor;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class AccountRecoveryCodeServiceTests
{
    [Fact]
    public async Task Codes_ShouldBeIndependentUserBoundOneUseAndRetainExhaustedMarker()
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        var other = await fixture.CreateUserAsync(scope.ServiceProvider);
        var service = new AccountRecoveryCodeService(db);
        Assert.Equal(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0), await service.GetStatusAsync(user.Id));
        var codes = (await service.GenerateAsync(user))!;
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches("^[A-F0-9]{4}(-[A-F0-9]{4}){7}$", code));
        var stored = (await db.UserTokens.SingleAsync()).Value!;
        Assert.All(codes, code => Assert.DoesNotContain(code.Replace("-", ""), stored));
        Assert.False(await service.RedeemAsync(other, codes[0], "op"));
        Assert.False(await service.RedeemAsync(user, new string('0', 32), "op"));
        Assert.Equal(10, (await service.GetStatusAsync(user.Id)).RemainingCodes);
        // An ordinary-factor removal rotates the stamp and removes OTP codes only.
        user.TwoFactorEnabled = false;
        user.RecoveryCodes = null;
        user.SecurityStamp = "after-otp-removal";
        await db.SaveChangesAsync();
        foreach (var code in codes) Assert.True(await service.RedeemAsync(user, code.ToLowerInvariant(), "op"));
        Assert.True(service.HasRedemptionProof(user, "op"));
        Assert.False(service.HasRedemptionProof(user, "other-op"));
        Assert.False(service.HasRedemptionProof(other, "op"));
        Assert.Equal(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(true, 0), await service.GetStatusAsync(user.Id));
        Assert.False(await service.RedeemAsync(user, codes[^1], "op"));
        Assert.False(service.HasRedemptionProof(user, "op"));
    }

    [Theory]
    [InlineData("redeem")]
    [InlineData("regenerate")]
    [InlineData("reset")]
    public async Task CompetingContext_ShouldInvalidateStaleRedemptionAndNotResurrectOnLaterSave(string winner)
    {
        using var fixture = new Fixture();
        await using var first = fixture.Services.CreateAsyncScope();
        await using var second = fixture.Services.CreateAsyncScope();
        var db = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(first.ServiceProvider);
        var service = new AccountRecoveryCodeService(db);
        var codes = (await service.GenerateAsync(user))!;
        var competingDb = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var competingUser = await competingDb.Users.SingleAsync();
        var competing = new AccountRecoveryCodeService(competingDb);
        if (winner == "redeem") Assert.True(await competing.RedeemAsync(competingUser, codes[0], "winner"));
        if (winner == "regenerate") Assert.NotNull(await competing.GenerateAsync(competingUser));
        if (winner == "reset")
        {
            competingUser.SecurityStamp = "reset";
            competingUser.ConcurrencyStamp = Guid.NewGuid().ToString();
            competingDb.UserTokens.RemoveRange(await competingDb.UserTokens.ToListAsync());
            await competingDb.SaveChangesAsync();
        }
        Assert.False(await service.RedeemAsync(user, codes[0], "loser"));
        Assert.False(service.HasRedemptionProof(user, "loser"));
        user.AccessFailedCount++;
        await db.SaveChangesAsync();
        var status = await service.GetStatusAsync(user.Id);
        Assert.Equal(winner == "redeem" ? 9 : winner == "regenerate" ? 10 : 0, status.RemainingCodes);
        Assert.Equal(winner != "reset", status.HasBatch);
        if (winner == "reset") Assert.Equal("reset", user.SecurityStamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWrite_ShouldDiscardTokenMutationsBeforeLaterIdentitySave(bool regeneration)
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        var service = new AccountRecoveryCodeService(db);
        var original = (await service.GenerateAsync(user))!;
        fixture.Failure.Fail = true;
        if (regeneration) Assert.Null(await service.GenerateAsync(user));
        else Assert.False(await service.RedeemAsync(user, original[0], "operation"));
        Assert.False(service.HasRedemptionProof(user, "operation"));
        fixture.Failure.Fail = false;
        user.AccessFailedCount++;
        await db.SaveChangesAsync();
        Assert.Equal(10, (await service.GetStatusAsync(user.Id)).RemainingCodes);
        Assert.True(await service.RedeemAsync(user, original[0], "retry"));
    }

    [Fact]
    public async Task Registration_ShouldIssueInitialBatchOnceAndNeverReplaceExhaustedBatch()
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        var registration = CreatePasskeys(scope.ServiceProvider);
        var first = await registration.RegisterCredentialsAsync(user, Attestation(), Options(user));
        Assert.True(first.Success);
        Assert.Equal(10, first.AccountRecoveryCodes!.Count);
        var service = new AccountRecoveryCodeService(db);
        foreach (var code in first.AccountRecoveryCodes) Assert.True(await service.RedeemAsync(user, code, "op"));
        var second = await registration.RegisterCredentialsAsync(user, Attestation(), Options(user));
        Assert.True(second.Success);
        Assert.Null(second.AccountRecoveryCodes);
        Assert.Equal(2, await db.UserCredentials.CountAsync());
        Assert.Equal(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(true, 0), await service.GetStatusAsync(user.Id));
    }

    [Fact]
    public async Task Registration_ShouldRollbackCredentialAndBatchOnFailedWriteWithoutLaterResurrection()
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        fixture.Failure.Fail = true;
        var result = await CreatePasskeys(scope.ServiceProvider).RegisterCredentialsAsync(user, Attestation(), Options(user));
        Assert.False(result.Success);
        Assert.Null(result.AccountRecoveryCodes);
        fixture.Failure.Fail = false;
        user.AccessFailedCount++;
        await db.SaveChangesAsync();
        Assert.Empty(await db.UserCredentials.ToListAsync());
        Assert.Empty(await db.UserTokens.ToListAsync());
    }

    [Theory]
    [InlineData("custom-live")]
    [InlineData("custom-empty")]
    [InlineData("native-live")]
    [InlineData("native-empty")]
    [InlineData("existing-otp")]
    public async Task Registration_ShouldPreserveLegacyAndExistingEnrollmentHistory(string history)
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        user.TwoFactorEnabled = history is "custom-live" or "native-live" or "existing-otp";
        if (history.StartsWith("custom")) user.RecoveryCodes = history.EndsWith("live") ? "[\"hashed-legacy\"]" : "[]";
        if (history.StartsWith("native")) db.UserTokens.Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id, LoginProvider = "[AspNetUserStore]", Name = "RecoveryCodes",
            Value = history.EndsWith("live") ? "abcde-fghij" : ""
        });
        await db.SaveChangesAsync();
        var status = await new AccountRecoveryCodeService(db).GetStatusAsync(user.Id);
        Assert.Equal(history != "existing-otp", status.HasBatch);
        Assert.Equal(0, status.RemainingCodes);
        Assert.Equal(history.EndsWith("live") ? 1 : 0, status.LegacyRemainingCodes);
        var result = await CreatePasskeys(scope.ServiceProvider).RegisterCredentialsAsync(user, Attestation(), Options(user));
        Assert.True(result.Success);
        Assert.Null(result.AccountRecoveryCodes);
        Assert.Equal(status, await new AccountRecoveryCodeService(db).GetStatusAsync(user.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotation_ShouldReplaceAllFamiliesTogetherAndRollbackAllTrackedStores(bool fail)
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        var service = new AccountRecoveryCodeService(db);
        var original = (await service.GenerateAsync(user))!;
        user.TwoFactorEnabled = true;
        user.RecoveryCodes = "[\"hashed-legacy\"]";
        db.UserTokens.Add(new IdentityUserToken<Guid> { UserId = user.Id, LoginProvider = "[AspNetUserStore]",
            Name = "RecoveryCodes", Value = "abcde-fghij" });
        await db.SaveChangesAsync();
        fixture.Failure.Fail = fail;
        var replacement = await service.GenerateAsync(user);
        fixture.Failure.Fail = false;
        user.AccessFailedCount++;
        await db.SaveChangesAsync();
        Assert.Equal(fail, replacement == null);
        Assert.Equal(fail ? "[\"hashed-legacy\"]" : "[]", user.RecoveryCodes);
        Assert.Equal(fail ? "abcde-fghij" : "", (await db.UserTokens.AsNoTracking().SingleAsync(token =>
            token.LoginProvider == "[AspNetUserStore]")).Value);
        Assert.Equal(fail, await service.RedeemAsync(user, original[0], "op"));
        Assert.True((await service.GetStatusAsync(user.Id)).HasLegacyBatch);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OtpEnrollment_ShouldCommitFirstBatchWithFactorAndRollbackFailedWrite(bool totp, bool fail)
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        const string code = "123456";
        user.EmailMfaCode = hasher.HashPassword(user, code);
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        await users.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        var attempts = new Mock<Core.Application.IEmailMfaAttemptStore>();
        attempts.Setup(value => value.TryReserveAttemptAsync(user.Id, It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Core.Application.EmailMfaAttemptReservation.Reserved);
        var mfa = new MfaService(users, Mock.Of<IBrandingService>(), Mock.Of<IEmailService>(), Mock.Of<Core.Application.Interfaces.IEmailTemplateService>(),
            hasher, Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(), Mock.Of<ISecurityPolicyService>(),
            attempts.Object, db, NullLogger<MfaService>.Instance);
        fixture.Failure.Fail = fail;
        var success = totp ? await mfa.VerifyAndEnableTotpAsync(user, CurrentTotp()) : await mfa.VerifyAndEnableEmailMfaAsync(user, code);
        fixture.Failure.Fail = false;
        user.AccessFailedCount++;
        await db.SaveChangesAsync();
        Assert.Equal(!fail, success);
        Assert.Equal(!fail && totp, user.TwoFactorEnabled);
        Assert.Equal(!fail && !totp, user.EmailMfaEnabled);
        var issued = mfa.TakeInitialRecoveryCodes(user);
        Assert.Equal(fail ? 0 : 10, issued?.Count ?? 0);
        Assert.Null(mfa.TakeInitialRecoveryCodes(user));
        Assert.Equal(fail ? 0 : 10, (await new AccountRecoveryCodeService(db).GetStatusAsync(user.Id)).RemainingCodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnifiedVerifier_ShouldAcceptLegacyOtpWithoutIndependentEnrollmentProof(bool native)
    {
        using var fixture = new Fixture();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await fixture.CreateUserAsync(scope.ServiceProvider);
        user.TwoFactorEnabled = true;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        const string code = "ABCDE12345";
        if (native) db.UserTokens.Add(new IdentityUserToken<Guid> { UserId = user.Id, LoginProvider = "[AspNetUserStore]",
            Name = "RecoveryCodes", Value = "ABCDE-12345" });
        else user.RecoveryCodes = JsonSerializer.Serialize(new[] { hasher.HashPassword(user, code) });
        await db.SaveChangesAsync();
        var mfa = new MfaService(users, Mock.Of<IBrandingService>(), Mock.Of<IEmailService>(), Mock.Of<Core.Application.Interfaces.IEmailTemplateService>(),
            hasher, Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(), Mock.Of<ISecurityPolicyService>(),
            Mock.Of<Core.Application.IEmailMfaAttemptStore>(), db, NullLogger<MfaService>.Instance);
        var service = new AccountRecoveryCodeService(db, mfa);
        Assert.True(await service.RedeemAsync(user, code, "account-recovery-enrollment"));
        Assert.False(service.HasRedemptionProof(user, "account-recovery-enrollment"));
        Assert.False(await service.RedeemAsync(user, code, "op"));
        var status = await service.GetStatusAsync(user.Id);
        Assert.True(status.HasBatch);
        Assert.Equal(0, status.TotalRemainingCodes);
    }

    private static string CurrentTotp()
    {
        var counter = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30));
        var hash = System.Security.Cryptography.HMACSHA1.HashData(Encoding.ASCII.GetBytes("12345678901234567890"), counter);
        var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static PasskeyService CreatePasskeys(IServiceProvider services)
    {
        var fido = new Mock<IFido2>();
        fido.Setup(value => value.MakeNewCredentialAsync(It.IsAny<MakeNewCredentialParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new RegisteredPublicKeyCredential
            {
                Type = PublicKeyCredentialType.PublicKey, Id = Guid.NewGuid().ToByteArray(), PublicKey = [2],
                SignCount = 0, AaGuid = Guid.Empty,
                User = new Fido2User { Id = [1], Name = "user", DisplayName = "user" },
                AttestationFormat = "none", AttestationObject = [], AttestationClientDataJson = [], Transports = []
            });
        return new(fido.Object, services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<ApplicationDbContext>(), NullLogger<PasskeyService>.Instance,
            Mock.Of<ISecurityPolicyService>());
    }

    private static string Options(ApplicationUser user) => JsonSerializer.Serialize(new
    {
        rp = new { id = "localhost", name = "Tests" },
        user = new { id = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(user.Id.ToString())), name = user.UserName, displayName = user.UserName },
        challenge = "AQ", pubKeyCredParams = new[] { new { type = "public-key", alg = -7 } }
    });

    private static string Attestation()
    {
        // The existing FIDO verification seam is mocked; Identity/SQLite writes are real.
        var authData = new byte[37]; authData[32] = 5;
        var writer = new CborWriter();
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt"); writer.WriteTextString("none");
        writer.WriteTextString("attStmt"); writer.WriteStartMap(0); writer.WriteEndMap();
        writer.WriteTextString("authData"); writer.WriteByteString(authData); writer.WriteEndMap();
        return JsonSerializer.Serialize(new
        {
            id = "AQ", rawId = "AQ", type = "public-key", response = new
            {
                attestationObject = WebEncoders.Base64UrlEncode(writer.Encode()),
                clientDataJSON = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"type\":\"webauthn.create\",\"challenge\":\"AQ\",\"origin\":\"https://localhost\"}"))
            }
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public FailWrite Failure { get; } = new();
        public ServiceProvider Services { get; }
        public Fixture()
        {
            _connection.Open();
            var services = new ServiceCollection().AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection).AddInterceptors(Failure));
            services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>().AddTokenProvider<AuthenticatorTokenProvider<ApplicationUser>>(TokenOptions.DefaultAuthenticatorProvider);
            Services = services.BuildServiceProvider();
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        }
        public async Task<ApplicationUser> CreateUserAsync(IServiceProvider services)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), IsActive = true };
            Assert.True((await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded);
            return user;
        }
        public void Dispose() { Services.Dispose(); _connection.Dispose(); }
    }

    private sealed class FailWrite : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new DbUpdateConcurrencyException("Test write conflict");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
