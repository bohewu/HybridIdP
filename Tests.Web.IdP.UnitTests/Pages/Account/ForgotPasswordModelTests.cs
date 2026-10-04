using System.Text;
using Core.Application;
using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Web.IdP.Extensions;
using Moq;
using Web.IdP;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class ForgotPasswordModelTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void RecoveryConfiguration_ShouldRequireSharedKeyForEitherNewCeremony(bool identity, bool selection, bool key, bool rejected)
    {
        var values = new Dictionary<string, string?>
        {
            ["RecoveryIdentityVerification:Enabled"] = identity.ToString(),
            ["RecoveryIdentityVerification:RequireForDirectoryAccounts"] = "true",
            ["RecoveryIdentityVerification:Endpoint"] = "https://provider.example.invalid/verify",
            ["RecoveryIdentityVerification:SharedSecret"] = Guid.NewGuid().ToString("N"),
            ["RecoveryEmailSelection:Enabled"] = selection.ToString(),
            ["RecoveryThrottle:HashKey"] = key ? Guid.NewGuid().ToString("N") : null
        };
        using var provider = new ServiceCollection().AddLogging().AddCustomApplicationServices(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RecoveryThrottleOptions>>();
        if (rejected) Assert.Throws<OptionsValidationException>(() => options.Value);
        else Assert.NotNull(options.Value);
    }

    [Fact]
    public void IdentityConfiguration_ShouldRequireExplicitAuthorityCohort()
    {
        var options = new RecoveryIdentityVerificationOptions { Enabled = true,
            Endpoint = "https://provider.example.invalid/verify", SharedSecret = Guid.NewGuid().ToString("N") };
        Assert.True(new RecoveryIdentityVerificationOptionsValidator().Validate(null, options).Failed);
        options.RequireForDirectoryAccounts = true;
        Assert.True(new RecoveryIdentityVerificationOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public async Task PrepareThenSend_ShouldClearEvidenceAndRequireExplicitSendWithStableContext()
    {
        var precheck = new Mock<IRecoveryPrecheckService>();
        precheck.SetupGet(p => p.Enabled).Returns(true);
        var grant = Guid.NewGuid();
        RecoveryPrepareRequest? prepare = null;
        RecoverySendOtpRequest? send = null;
        precheck.Setup(p => p.PrepareAsync(It.IsAny<RecoveryPrepareRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecoveryPrepareRequest, CancellationToken>((request, _) => prepare = request)
            .ReturnsAsync(new RecoveryPrepareResult(grant, "c***@example.test"));
        precheck.Setup(p => p.SendOtpAsync(It.IsAny<RecoverySendOtpRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecoverySendOtpRequest, CancellationToken>((request, _) => send = request)
            .ReturnsAsync(new NativeRecoveryStartResult(Guid.NewGuid()));
        var f = new Fixture(ForgotPasswordMode.Native, precheck: precheck.Object);
        f.Model.Identifier.Value = "user";
        f.Model.Identifier.IdentityIdentifier = "  synthetic-id  ";
        f.Model.ModelState.SetModelValue("Identifier.IdentityIdentifier", "  synthetic-id  ", "  synthetic-id  ");
        Assert.IsType<PageResult>(await f.Model.OnPostStartAsync(default));
        Assert.True(f.Model.ReadyToSend);
        Assert.False(f.Model.AwaitingCode);
        Assert.False(f.Model.AwaitingPassword);
        Assert.Equal("  synthetic-id  ", prepare!.IdentityIdentifier);
        Assert.Null(f.Model.Identifier.IdentityIdentifier);
        Assert.Empty(f.Model.ModelState);
        Assert.DoesNotContain(f.Session.TextValues, value => value.Contains("synthetic-id"));
        precheck.Verify(p => p.SendOtpAsync(It.IsAny<RecoverySendOtpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        f.ProofService.VerifyNoOtherCalls();
        Assert.IsType<PageResult>(await f.Model.OnPostSendCodeAsync(default));
        Assert.Equal(grant, send!.GrantId);
        Assert.Equal(prepare.Context, send.Context);
        Assert.True(f.Model.AwaitingCode);
        Assert.False(f.Model.AwaitingPassword);
        Assert.False(f.Model.User.Identity?.IsAuthenticated ?? false);
        await f.Model.OnPostSendCodeAsync(default);
        precheck.Verify(p => p.SendOtpAsync(It.IsAny<RecoverySendOtpRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BrowserClaimedVerificationWithoutServerGrant_ShouldNotSendOrReset()
    {
        var precheck = new Mock<IRecoveryPrecheckService>();
        precheck.SetupGet(p => p.Enabled).Returns(true);
        var f = new Fixture(ForgotPasswordMode.Native, precheck: precheck.Object);
        f.Model.ModelState.SetModelValue("Verified", "true", "true");
        f.Model.ModelState.SetModelValue("recipient", "attacker@example.test", "attacker@example.test");
        await f.Model.OnPostSendCodeAsync(default);
        await f.Model.OnPostResetAsync(default);
        precheck.Verify(p => p.SendOtpAsync(It.IsAny<RecoverySendOtpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        f.ResetService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnGetAsync_ExternalRuntimeMode_ReturnsNotFound()
    {
        var fixture = new Fixture(ForgotPasswordMode.External);

        var result = await fixture.Model.OnGetAsync();

        Assert.IsType<NotFoundResult>(result);
        fixture.ProofService.VerifyNoOtherCalls();
        fixture.ResetService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnGetAsync_CustomPolicy_ExposesOnlyEnabledClientCheckableRules()
    {
        var fixture = new Fixture(
            ForgotPasswordMode.Native,
            new SecurityPolicy
            {
                ForgotPasswordMode = ForgotPasswordMode.Native,
                MinPasswordLength = 10,
                MinCharacterTypes = 0,
                RequireUppercase = false,
                RequireLowercase = true,
                RequireDigit = false,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 5
            });

        var result = await fixture.Model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(
            ["minimum-length", "lowercase", "symbol"],
            fixture.Model.ClientPasswordRules.Select(rule => rule.Name));
        Assert.DoesNotContain(
            fixture.Model.ClientPasswordRules,
            rule => rule.Name == "password-history");
    }

    [Fact]
    public async Task RecoveryFlow_VerifiedProofAndPassword_RemainOffBrowserStateAndCompletesWithoutSignIn()
    {
        var fixture = new Fixture(ForgotPasswordMode.Native);
        var requestId = Guid.NewGuid();
        NativeRecoveryStartRequest? startRequest = null;
        NativeRecoveryResetRequest? resetRequest = null;
        fixture.ProofService
            .Setup(service => service.StartAsync(
                It.IsAny<NativeRecoveryStartRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<NativeRecoveryStartRequest, CancellationToken>((request, _) => startRequest = request)
            .ReturnsAsync(new NativeRecoveryStartResult(requestId));
        fixture.ProofService
            .Setup(service => service.VerifyAsync(
                It.IsAny<NativeRecoveryVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NativeRecoveryVerificationResult(
                NativeRecoveryVerificationOutcome.Verified,
                "opaque-proof"));
        fixture.ResetService
            .Setup(service => service.ResetAsync(
                It.IsAny<NativeRecoveryResetRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<NativeRecoveryResetRequest, CancellationToken>((request, _) => resetRequest = request)
            .ReturnsAsync(new NativeRecoveryResetResult(NativeRecoveryResetOutcome.Succeeded));

        fixture.Model.Identifier.Value = "local.user@example.test";
        var startResult = await fixture.Model.OnPostStartAsync(CancellationToken.None);

        Assert.IsType<PageResult>(startResult);
        Assert.True(fixture.Model.AwaitingCode);
        Assert.Equal("local.user@example.test", startRequest?.Identifier);
        Assert.DoesNotContain("local.user@example.test", fixture.Session.TextValues);

        fixture.Model.Verification.Code = "123456";
        var verifyResult = await fixture.Model.OnPostVerifyAsync(CancellationToken.None);

        Assert.IsType<PageResult>(verifyResult);
        Assert.True(fixture.Model.AwaitingPassword);
        Assert.DoesNotContain("opaque-proof", fixture.Session.TextValues);

        fixture.Model.Password.NewPassword = "New!Password123";
        fixture.Model.Password.ConfirmPassword = "New!Password123";
        var resetResult = await fixture.Model.OnPostResetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(resetResult);
        Assert.True(fixture.Model.RecoverySucceeded);
        Assert.Equal("opaque-proof", resetRequest?.Proof);
        Assert.Equal("New!Password123", resetRequest?.NewPassword);
        Assert.Empty(fixture.Session.TextValues);
        Assert.Null(fixture.Model.Password.NewPassword);
        Assert.Null(fixture.Model.Password.ConfirmPassword);
    }

    [Fact]
    public async Task OnPostResetAsync_RuntimeModeChangedToExternal_DeniesBeforeReset()
    {
        var fixture = new Fixture(ForgotPasswordMode.External);
        fixture.Model.Password.NewPassword = "New!Password123";
        fixture.Model.Password.ConfirmPassword = "New!Password123";

        var result = await fixture.Model.OnPostResetAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        fixture.ResetService.VerifyNoOtherCalls();
        Assert.Null(fixture.Model.Password.NewPassword);
        Assert.Null(fixture.Model.Password.ConfirmPassword);
    }

    [Fact]
    public async Task OnPostResetAsync_AllowlistedPasswordErrors_ReturnsCurrentPolicyGuidance()
    {
        var fixture = new Fixture(ForgotPasswordMode.Native);
        await fixture.AdvanceToPasswordAsync();
        fixture.ResetService
            .Setup(service => service.ResetAsync(
                It.IsAny<NativeRecoveryResetRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NativeRecoveryResetResult(
                NativeRecoveryResetOutcome.PasswordRejected,
                ["PasswordTooShort", "PasswordRequiresNonAlphanumeric", "PasswordReuse"]));
        fixture.Model.Password.NewPassword = "GeneratedPassword";
        fixture.Model.Password.ConfirmPassword = "GeneratedPassword";

        var result = await fixture.Model.OnPostResetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(fixture.Model.AwaitingPassword);
        Assert.Equal(12, fixture.Model.CurrentPolicy?.MinPasswordLength);
        Assert.Equal(7, fixture.Model.CurrentPolicy?.PasswordHistoryCount);
        Assert.Equal(
            [
                "NativeRecovery.Error.MinimumLength:12",
                "NativeRecovery.Error.Symbol",
                "NativeRecovery.Error.PasswordReuse:7"
            ],
            ModelErrors(fixture.Model));
        Assert.Null(fixture.Model.Password.NewPassword);
        Assert.Null(fixture.Model.Password.ConfirmPassword);
    }

    [Fact]
    public async Task OnPostResetAsync_NonPasswordIdentityError_ReturnsNeutralRecoveryFailure()
    {
        var fixture = new Fixture(ForgotPasswordMode.Native);
        await fixture.AdvanceToPasswordAsync();
        fixture.ResetService
            .Setup(service => service.ResetAsync(
                It.IsAny<NativeRecoveryResetRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NativeRecoveryResetResult(
                NativeRecoveryResetOutcome.PasswordRejected,
                ["ConcurrencyFailure"]));
        fixture.Model.Password.NewPassword = "Generated!Password123";
        fixture.Model.Password.ConfirmPassword = "Generated!Password123";

        var result = await fixture.Model.OnPostResetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(["NativeRecovery.RequestFailed"], ModelErrors(fixture.Model));
    }

    private static string[] ModelErrors(ForgotPasswordModel model) =>
        model.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .ToArray();

    private sealed class Fixture
    {
        public Mock<INativePasswordRecoveryProofService> ProofService { get; } = new();
        public Mock<INativePasswordRecoveryResetService> ResetService { get; } = new();
        public TestSession Session { get; } = new();
        public ForgotPasswordModel Model { get; }

        public Fixture(ForgotPasswordMode runtimeMode, SecurityPolicy? customPolicy = null, IRecoveryPrecheckService? precheck = null)
        {
            var policy = customPolicy ?? new SecurityPolicy
            {
                ForgotPasswordMode = runtimeMode,
                MinPasswordLength = 12,
                MinCharacterTypes = 3,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 7
            };
            var policyService = new Mock<ISecurityPolicyService>();
            policyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(policy);
            var localizer = new Mock<IStringLocalizer<SharedResource>>();
            localizer
                .Setup(service => service[It.IsAny<string>()])
                .Returns((string name) => new LocalizedString(name, name));
            localizer
                .Setup(service => service[It.IsAny<string>(), It.IsAny<object[]>()])
                .Returns((string name, object[] arguments) =>
                    new LocalizedString(name, $"{name}:{string.Join(",", arguments)}"));
            var evaluator = new ForgotPasswordRoutingEvaluator(
                Microsoft.Extensions.Options.Options.Create(new ForgotPasswordRecoveryOptions
                {
                    DeploymentCeiling = ForgotPasswordMode.Native,
                    NativeRecoveryEnabled = true
                }));

            Model = new ForgotPasswordModel(
                ProofService.Object,
                ResetService.Object,
                policyService.Object,
                evaluator,
                new EphemeralDataProtectionProvider(),
                localizer.Object, precheck: precheck)
            {
                PageContext = new PageContext
                {
                    HttpContext = new DefaultHttpContext { Session = Session }
                }
            };
        }

        public async Task AdvanceToPasswordAsync()
        {
            ProofService
                .Setup(service => service.StartAsync(
                    It.IsAny<NativeRecoveryStartRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NativeRecoveryStartResult(Guid.NewGuid()));
            ProofService
                .Setup(service => service.VerifyAsync(
                    It.IsAny<NativeRecoveryVerificationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NativeRecoveryVerificationResult(
                    NativeRecoveryVerificationOutcome.Verified,
                    "opaque-proof"));

            Model.Identifier.Value = "local.user@example.test";
            await Model.OnPostStartAsync(CancellationToken.None);
            Model.Verification.Code = "123456";
            await Model.OnPostVerifyAsync(CancellationToken.None);
        }
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _values.Keys;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsAvailable => true;
        public IEnumerable<string> TextValues =>
            _values.Values.Select(value => Encoding.UTF8.GetString(value));

        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;

        public bool TryGetValue(string key, out byte[] value) =>
            _values.TryGetValue(key, out value!);
    }
}
