using Core.Domain.Enums;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ForgotPasswordRoutingEvaluatorTests
{
    [Fact]
    public void Evaluate_DefaultExternalModeWithValidHttpUrl_PreservesExistingExternalEntry()
    {
        var evaluator = CreateEvaluator();

        var decision = evaluator.Evaluate(
            ForgotPasswordMode.External,
            "https://recovery.example.test/forgot-password");

        Assert.Equal(ForgotPasswordMode.External, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.External, decision.AvailableMode);
        Assert.Equal("https://recovery.example.test/forgot-password", decision.ExternalUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@recovery.example.test/")]
    public void Evaluate_ExternalModeWithMissingOrUnsafeUrl_IsUnavailable(string? externalUrl)
    {
        var decision = CreateEvaluator().Evaluate(ForgotPasswordMode.External, externalUrl);

        Assert.Equal(ForgotPasswordMode.External, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.AvailableMode);
        Assert.Null(decision.ExternalUrl);
    }

    [Fact]
    public void Evaluate_DisabledModeWithValidExternalUrl_IsUnavailable()
    {
        var decision = CreateEvaluator().Evaluate(
            ForgotPasswordMode.Disabled,
            "https://recovery.example.test/forgot-password");

        Assert.Equal(ForgotPasswordMode.Disabled, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.AvailableMode);
        Assert.Null(decision.ExternalUrl);
    }

    [Fact]
    public void Evaluate_NativeModeWithCompleteNativeHandler_IsAvailable()
    {
        var decision = CreateEvaluator(
            ForgotPasswordMode.Native,
            nativeRecoveryEnabled: true).Evaluate(
                ForgotPasswordMode.Native,
                "https://recovery.example.test/forgot-password");

        Assert.Equal(ForgotPasswordMode.Native, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Native, decision.AvailableMode);
        Assert.Null(decision.ExternalUrl);
    }

    [Fact]
    public void Evaluate_NativeModeWithHandlerDisabled_IsUnavailable()
    {
        var decision = CreateEvaluator(ForgotPasswordMode.Native).Evaluate(
            ForgotPasswordMode.Native,
            "https://recovery.example.test/forgot-password");

        Assert.Equal(ForgotPasswordMode.Native, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.AvailableMode);
        Assert.Null(decision.ExternalUrl);
    }

    [Fact]
    public void Evaluate_NativeModeAboveDefaultExternalCeiling_FailsClosed()
    {
        var decision = CreateEvaluator().Evaluate(ForgotPasswordMode.Native, null);

        Assert.Equal(ForgotPasswordMode.External, decision.DeploymentCeiling);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.AvailableMode);
    }

    [Fact]
    public void Evaluate_NativeModeAtNativeCeiling_RemainsUnavailableWithoutNativeHandler()
    {
        var decision = CreateEvaluator(ForgotPasswordMode.Native)
            .Evaluate(ForgotPasswordMode.Native, null);

        Assert.Equal(ForgotPasswordMode.Native, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.AvailableMode);
        Assert.Null(decision.ExternalUrl);
    }

    [Fact]
    public void Evaluate_InvalidRuntimeMode_FailsClosed()
    {
        var decision = CreateEvaluator(ForgotPasswordMode.Native)
            .Evaluate((ForgotPasswordMode)999, "https://recovery.example.test/");

        Assert.Equal(ForgotPasswordMode.Disabled, decision.RuntimeMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.PermittedMode);
        Assert.Equal(ForgotPasswordMode.Disabled, decision.AvailableMode);
    }

    [Fact]
    public void OptionsValidator_InvalidDeploymentCeiling_Fails()
    {
        var result = new ForgotPasswordRecoveryOptionsValidator().Validate(
            null,
            new ForgotPasswordRecoveryOptions { DeploymentCeiling = (ForgotPasswordMode)999 });

        Assert.False(result.Succeeded);
    }

    private static ForgotPasswordRoutingEvaluator CreateEvaluator(
        ForgotPasswordMode deploymentCeiling = ForgotPasswordMode.External,
        bool nativeRecoveryEnabled = false) =>
        new(Options.Create(new ForgotPasswordRecoveryOptions
        {
            DeploymentCeiling = deploymentCeiling,
            NativeRecoveryEnabled = nativeRecoveryEnabled
        }));
}
